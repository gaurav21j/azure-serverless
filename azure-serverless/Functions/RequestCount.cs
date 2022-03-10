using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Cosmos;
using AzFunction_Serverless.Model.Cosmos;
using Microsoft.Azure.Cosmos.Table;
using AzFunction_Serverless.Model.Storage;
using Azure.Security.KeyVault.Secrets;
using Azure.Identity;

namespace AzFunction_Serverless
{
    public class RequestCount
    {
        private static CosmosClient client;

        private static string cosmosUrl;

        private static string cosmosKey;

        private static string databaseName;

        private static string collectionName;

        private static string storageConnectionString;
        
        [FunctionName("RequestCount")]
        public static async Task<IActionResult> Run( [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req, ILogger log) 
        {
            /**
             * We're trying to count the number of requests we get for a particular site.
             * We use a DB that is maintaining a count. Once the request comes. Get the current value of count. 
             * Increment it by one, return that, and store the updated value back in the DB.
             */

            log.LogInformation("C# HTTP trigger function processed a request.");
            Initialization();
            
            //Storage Table - Crud ops method.
            string storageCount = GetCountFromStorage();
            
            string val = await GetCount();
            int count = Int32.Parse(val) + 1;
            await UpdateCountAsync(count);
            return new OkObjectResult(count.ToString());
        }

        public static void Initialization()
        {
            cosmosUrl = Environment.GetEnvironmentVariable("CosmosUrl");
            cosmosKey = GetSecretFromKey(Environment.GetEnvironmentVariable("CosmosKey"));
            databaseName = Environment.GetEnvironmentVariable("DatabaseName");
            collectionName = Environment.GetEnvironmentVariable("CollectionName");
            storageConnectionString = GetSecretFromKey(Environment.GetEnvironmentVariable("StorageConnectionString"));
        }

        private static string GetCountFromStorage()
        {
            var tableName = "CountTable";
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnectionString);
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient(new TableClientConfiguration());
            CloudTable table = tableClient.GetTableReference(tableName);
            return QueryStorage(table);            
        }

        public static string QueryStorage(CloudTable table)
        {
            TableQuery<CountEntity> query = new TableQuery<CountEntity>();
            foreach (CountEntity entity in table.ExecuteQuery(query))
                return entity.Count;
            return 0.ToString();
        }

        public static CosmosClient GetCosmosClient()
        {
            client = new CosmosClient(cosmosUrl, cosmosKey);            
            return client;
        }

        public async static Task<string> GetCount()
        {
            client = (client == null) ? GetCosmosClient() : client;
            var container = client.GetContainer(databaseName, collectionName);
            var sqlQueryText = "SELECT * FROM c";
            QueryDefinition queryDefinition = new QueryDefinition(sqlQueryText);
            FeedIterator<Count> queryResultSetIterator = container.GetItemQueryIterator<Count>(queryDefinition);

            while (queryResultSetIterator.HasMoreResults)
            {
                FeedResponse<Count> currentResultSet = await queryResultSetIterator.ReadNextAsync();
                foreach (Count count in currentResultSet)
                {
                    return count.id;
                }
            }
            return 0.ToString();
        }


        public static async Task UpdateCountAsync(int count)
        {
            /**
             * Cosmos does not support update of specific documents. 
             * Delete the prev document id, and create a new one with id+1.
             * It's unnecessary processing. Update -> Delete + Insert. 
             * Better storage options should be explored for simple ops like maintaining count.
             */

            client = (client == null) ? GetCosmosClient() : client;
            var container = client.GetContainer(databaseName, collectionName);            
            string partitionKeyValue = (count-1).ToString();
            await container.DeleteItemAsync<Count>(partitionKeyValue, new PartitionKey(partitionKeyValue));
            Count insertItem = new Count(count.ToString());            
            await container.CreateItemAsync<Count>(insertItem, new PartitionKey(insertItem.id));
            return;
        }


        // KeyVault connection. Make sure you're logged in with the account you've granted access to on kv.
        public static string GetSecretFromKey(string key)
        {
            try 
            {
                string keyVaultName = Environment.GetEnvironmentVariable("KEY_VAULT_NAME");
                var kvUri = "https://" + keyVaultName + ".vault.azure.net";
                SecretClient secretClient = GetSecretClient(kvUri);
                KeyVaultSecret secret = secretClient.GetSecretAsync(key, null).Result;
                return secret.Value;
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }

        public static SecretClient GetSecretClient(string KVUrl)
        {
            DefaultAzureCredential credentials = new DefaultAzureCredential();
            SecretClient secretClient = new SecretClient(new Uri(KVUrl), credentials);
            return secretClient;
        }

    }

}

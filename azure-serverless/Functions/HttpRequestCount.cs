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
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Azure.KeyVault;
using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;
using System.Text;
using Microsoft.Azure.ServiceBus;

namespace AzFunction_Serverless
{
    public class HttpRequestCount
    {
        private static CosmosClient client;

        private static string cosmosUrl;

        private static string cosmosKey;

        private static string databaseName;

        private static string collectionName;

        private static string storageConnectionString;

        private static string serviceBusConnectionString;

        private static string queueName;
        
        [FunctionName("HttpRequestCount")]
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

            //Sending message on servicebus.
            await SendMessageOnQueue(storageCount);

            //string val = await GetCount();
            //int count = Int32.Parse(val) + 1;
            //await UpdateCountAsync(count);
            //return new OkObjectResult(count.ToString());

            return new OkObjectResult(storageCount);
        }

        public static void Initialization()
        {
            cosmosUrl = Environment.GetEnvironmentVariable("CosmosUrl");
            cosmosKey = GetSecretFromKey(Environment.GetEnvironmentVariable("CosmosKey"));
            databaseName = Environment.GetEnvironmentVariable("DatabaseName");
            collectionName = Environment.GetEnvironmentVariable("CollectionName");
            storageConnectionString = GetSecretFromKey(Environment.GetEnvironmentVariable("StorageConnectionString"));
            serviceBusConnectionString = Environment.GetEnvironmentVariable("ServiceBusConnectionString");
            queueName = Environment.GetEnvironmentVariable("QueueName");
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


        public static string GetSecretFromKey(string key)
        {
            /**
             * KeyVault connection. Make sure you're logged in to the account you've granted access to on kv.
             * The user needs to have access provisioned on kv. 
             * When deployed on portal, any compute service should have managed identity enabled.
             * The identity of the Az service using the kv should have access policy added.             
             */

            try
            {
                string keyVaultName = Environment.GetEnvironmentVariable("KEY_VAULT_NAME");
                var kvUri = "https://" + keyVaultName + ".vault.azure.net";
                KeyVaultClient keyVaultClient = GetKeyVaultClient(kvUri);
                var secret = keyVaultClient.GetSecretAsync(kvUri, key).Result;
                return secret.Value;

                /**
                 * Research why this dint work. Difference between both these libraries.
                 * SecretClient secretClient = GetSecretClient(kvUri);
                 * KeyVaultSecret secret = secretClient.GetSecretAsync(key, null).Result;
                 * return secret.Value;
                 */
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }

        public static KeyVaultClient GetKeyVaultClient(string KVUrl)
        {
            var azureServiceTokenProvider = new AzureServiceTokenProvider();
            var keyVaultClient = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider.KeyVaultTokenCallback));
            return keyVaultClient;
        }

        public static SecretClient GetSecretClient(string KVUrl)
        {
            DefaultAzureCredential credentials = new DefaultAzureCredential();
            SecretClient secretClient = new SecretClient(new Uri(KVUrl), credentials);
            return secretClient;
        }

        public async static Task SendMessageOnQueue(string count)
        {
            try
            {
                string MessageBody = JsonConvert.SerializeObject(new Count(count));
                QueueClient client = new QueueClient(serviceBusConnectionString, queueName);
                Message Message = new Message(Encoding.UTF8.GetBytes(MessageBody));
                await client.SendAsync(Message);
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
            }            
        }

    }

}

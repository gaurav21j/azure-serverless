using System.Text;
using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using AzFunction_Serverless.Model.Cosmos;
using Newtonsoft.Json;
using Microsoft.Azure.ServiceBus.InteropExtensions;

namespace AzFunction_Serverless.Functions
{
    public class QueueProcessing
    {
        [FunctionName("QueueProcessing")]
        public async Task RunAsync([ServiceBusTrigger("%QueueName%", Connection = "ServiceBusConnectionString")] Message message, ILogger log)
        {
            try
            {                
                var body = message.GetBody<byte[]>();
                Count count = JsonConvert.DeserializeObject<Count>(Encoding.UTF8.GetString(body));
                log.LogInformation(count.id);
            }
            catch (Exception ex)
            {
                log.LogError(ex.Message);
            }
        }
    }
}

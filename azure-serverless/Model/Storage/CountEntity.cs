using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
using System.Text;

namespace AzFunction_Serverless.Model.Storage
{
    public class CountEntity : TableEntity
    {
        public CountEntity() { }

        public CountEntity(string PartitionKey, string RowKey)
        {
            this.PartitionKey = PartitionKey;
            this.RowKey = RowKey;
        }

        public string Count { get; set; }

    }
}

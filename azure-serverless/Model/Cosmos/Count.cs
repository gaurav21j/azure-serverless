using System;
using System.Collections.Generic;
using System.Text;

namespace AzFunction_Serverless.Model.Cosmos
{
    public class Count
    {
        public string id { get; set; }
        public Count(string id)
        {
            this.id = id;
        }
    }
}

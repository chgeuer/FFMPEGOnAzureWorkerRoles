//
// This is sample code from Christian Geuer-Pollmann (@chgeuer). Use it for whatever you like. 
//
namespace FFMPEGLib
{
    using System;
    using System.Collections.Generic;

    using Microsoft.WindowsAzure.Storage;
    using Microsoft.WindowsAzure.Storage.Table;

    public class LogTable
    {
        CloudTable table;

        public LogTable(CloudStorageAccount account, string tablename)
        {
            var tableClient = account.CreateCloudTableClient();
            table = tableClient.GetTableReference(tablename);
            table.CreateIfNotExists();
        }

        public void Log(string rowKey, IDictionary<string,string> values)
        {
            var entity = new DynamicTableEntity(partitionKey: "jobs", rowKey: rowKey); // 
            
            foreach (var kvp in values)
            {
                entity.Properties[kvp.Key] = new EntityProperty(kvp.Value);
            }

            table.Execute(TableOperation.InsertOrReplace(entity));
        }

        public void Enumerate(Action<DateTimeOffset, string, string> process)
        {
            TableContinuationToken token = null;
            TableRequestOptions reqOptions = new TableRequestOptions() { };
            OperationContext ctx = new OperationContext() { ClientRequestID = "" };
            while (true)
            {
                // CloudTable table = cloudTableClient.GetTableReference("MyTable");
                TableQuery<DynamicTableEntity> query = (new TableQuery<DynamicTableEntity>()).Take(100);
                System.Threading.ManualResetEvent evt = new System.Threading.ManualResetEvent(false);
                var result = table.BeginExecuteQuerySegmented<DynamicTableEntity>(query, token, reqOptions, ctx, (o) =>
                {
                    var response = (o.AsyncState as CloudTable).EndExecuteQuerySegmented<DynamicTableEntity>(o);
                    token = response.ContinuationToken;

                    foreach (var x in response)
                    {

                        var json = x.Properties["json"].StringValue;
                        var output = x.Properties["output"].StringValue;
                        process(x.Timestamp, json, output);
                    }

                    evt.Set();
                }, table);
                evt.WaitOne();
                if (token == null)
                {
                    break;
                }
            }
        }
    }
}

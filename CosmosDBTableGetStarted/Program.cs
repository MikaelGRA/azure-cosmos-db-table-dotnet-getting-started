﻿namespace TableSBS
{
    using Microsoft.Azure.CosmosDB.Table;
    using Microsoft.Azure.Storage;
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Diagnostics;
    using System.Linq;

    /// <summary>
    /// This sample program shows how to use the Azure Cosmos DB Table API with .NET
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Run common Table CRUD and query operations using the Azure Cosmos DB Table API endpoints
        /// </summary>
        /// <param name="args">Command line arguments</param>
        public static void Main(string[] args)
        {
            string connectionString = ConfigurationManager.AppSettings["CosmosDBStorageConnectionString"];
            Boolean useAzureTablestorage = (args.Length >= 1 && args[0] == "Standard");
            if (useAzureTablestorage)
            {
                connectionString = ConfigurationManager.AppSettings["StandardStorageConnectionString"];
            }

            int numIterations = 100;
            if (args.Length >= 2)
            {
                numIterations = int.Parse(args[1]);
            }
            
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);

            TableConnectionPolicy tableConnectionPolicy = new TableConnectionPolicy();
            tableConnectionPolicy.EnableEndpointDiscovery = true;
            tableConnectionPolicy.UseDirectMode = true;
            tableConnectionPolicy.UseTcpProtocol = true;

            CloudTableClient tableClient = useAzureTablestorage ?
                storageAccount.CreateCloudTableClient() :
                storageAccount.CreateCloudTableClient(tableConnectionPolicy, Microsoft.Azure.CosmosDB.ConsistencyLevel.Session);
            
            Program p = new Program();

            p.Run(tableClient, numIterations, useAzureTablestorage);
        }

        /// <summary>
        /// Run a bunch of core Table operations. Each operation is run ~100 times to measure latency. You can swap the endpoint and 
        /// compare with regular Azure Table storage.
        /// </summary>
        /// <param name="tableClient">The Azure Table storage client</param>
        /// <param name="numIterations">Number of iterations</param>
        /// <param name="useAzureTablestorage">If an Azure Table storage connection string is being used</param>
        public void Run(CloudTableClient tableClient, int numIterations, Boolean useAzureTablestorage)
        {
            Console.WriteLine("Creating Table if it doesn't exist...");
            
            CloudTable table = tableClient.GetTableReference("people");
            if (useAzureTablestorage) {
                table.CreateIfNotExists();
            } else
            {
                // Azure Cosmos DB supports a reserved throughput model. You can configure the default throughput per table by 
                // configuring the AppSetting for "TableThroughput" in terms of RU (request units) per second. 1 RU = 1 read of a 1KB document.
                // All operations are expressed in terms of RUs based on their CPU, memory, and IOPS consumption.
                // The number of RUs to be used by the table can be submitted as an argument here.
                table.CreateIfNotExists(throughput: 400);
            }

            List<CustomerEntity> items = new List<CustomerEntity>();
            List<double> latencies = new List<double>();
            Stopwatch watch = new Stopwatch();

            Console.WriteLine("Running inserts: ");
            for (int i = 0; i < numIterations; i++)
            {
                watch.Start();

                CustomerEntity item = new CustomerEntity()
                {
                    PartitionKey = Guid.NewGuid().ToString(),
                    RowKey = Guid.NewGuid().ToString(),
                    Email = $"{GetRandomString(6)}@contoso.com",
                    PhoneNumber = "425-555-0102",
                    Bio = GetRandomString(1000)
                };
                
                // Azure Cosmos DB is designed for guaranteed low latency at any scale, across the world
                // Writes in Azure Cosmos DB complete <10ms at p99 and ~6ms at p50. These are sychronously replicated, 
                // durably committed, and all content indexed. 
                // Latency is for reads for app in the same region as one of the Azure Cosmos DB regions
                // When the Table API is generally available, these latency guarantees are backed by SLAs
                TableOperation insertOperation = TableOperation.Insert(item);
                table.Execute(insertOperation);
                double latencyInMs = watch.Elapsed.TotalMilliseconds;

                Console.Write($"\r\tInsert #{i + 1} completed in {latencyInMs} ms.");
                items.Add(item);
                latencies.Add(latencyInMs);

                watch.Reset();
            }

            latencies.Sort();
            Console.WriteLine($"\n\tp0:{latencies[0]}, p50: {latencies[(int)(numIterations * 0.50)]}, p90: {latencies[(int)(numIterations * 0.90)]}. p99: {latencies[(int)(numIterations * 0.99)]}");
            Console.WriteLine("\n");

            Console.WriteLine("Running retrieves: ");
            latencies.Clear();

            for (int i = 0; i < numIterations; i++)
            {
                watch.Start();

                // Retrieves in Azure Cosmos DB complete <10ms at p99 and ~1ms at p50.
                // Latency is for reads for app in the same region as one of the Azure Cosmos DB regions
                // When the Table API is generally available, these latency guarantees are backed by SLAs
                TableOperation retrieveOperation = TableOperation.Retrieve<CustomerEntity>(items[i].PartitionKey, items[i].RowKey);
                table.Execute(retrieveOperation);
                double latencyInMs = watch.Elapsed.TotalMilliseconds;

                Console.Write($"\r\tRetrieve #{i + 1} completed in {latencyInMs} ms");
                latencies.Add(latencyInMs);

                watch.Reset();
            }

            latencies.Sort();
            Console.WriteLine($"\n\tp0:{latencies[0]}, p50: {latencies[(int)(numIterations * 0.50)]}, p90: {latencies[(int)(numIterations * 0.90)]}. p99: {latencies[(int)(numIterations * 0.99)]}");
            Console.WriteLine("\n");

            Console.WriteLine("Running query against secondary index: ");
            latencies.Clear();

            for (int i = 0; i < numIterations; i++)
            {
                watch.Start();

                // Query against any property using the index. Since Azure Cosmos DB supports automatic secondary indexes,
                // This query completes within milliseconds. The query performance difference is much more pronounced when you 
                // have 1000s-millions of documents.
                TableQuery<CustomerEntity> rangeQuery = new TableQuery<CustomerEntity>().Where(
                    TableQuery.GenerateFilterCondition("Email", QueryComparisons.Equal, items[i].Email));

                int count = 0;
                foreach (CustomerEntity entity in table.ExecuteQuery(rangeQuery))
                {
                    // Process query results
                    count++;
                }

                double latencyInMs = watch.Elapsed.TotalMilliseconds;
                Console.Write($"\r\tQuery #{i + 1} completed in {latencyInMs} ms");
                latencies.Add(latencyInMs);

                watch.Reset();
            }

            latencies.Sort();
            Console.WriteLine($"\n\tp0:{latencies[0]}, p50: {latencies[(int)(numIterations * 0.50)]}, p90: {latencies[(int)(numIterations * 0.90)]}. p99: {latencies[(int)(numIterations * 0.99)]}");
            Console.WriteLine("\n");

            Console.WriteLine("Running replace: ");
            latencies.Clear();

            for (int i = 0; i < numIterations; i++)
            {
                watch.Start();

                // Same latency as inserts, p99 < 15ms, and p50 < 6ms
                items[i].PhoneNumber = "425-555-5555";
                TableOperation replaceOperation = TableOperation.Replace(items[i]);
                table.Execute(replaceOperation);

                double latencyInMs = watch.Elapsed.TotalMilliseconds;
                Console.Write($"\r\tReplace #{i + 1} completed in {latencyInMs} ms");
                latencies.Add(latencyInMs);

                watch.Reset();
            }

            latencies.Sort();
            Console.WriteLine($"\n\tp0:{latencies[0]}, p50: {latencies[(int)(numIterations * 0.50)]}, p90: {latencies[(int)(numIterations * 0.90)]}. p99: {latencies[(int)(numIterations * 0.99)]}");
            Console.WriteLine("\n");

            #region DeleteTable
            Console.WriteLine("Running deletes: ");
            latencies.Clear();

            for (int i = 0; i < numIterations; i++)
            {
                watch.Start();

                // Same latency as inserts, p99 < 15ms, and p50 < 6ms
                TableOperation deleteOperation = TableOperation.Delete(items[i]);
                table.Execute(deleteOperation);

                double latencyInMs = watch.Elapsed.TotalMilliseconds;
                Console.Write($"\r\tDelete #{i + 1} completed in {latencyInMs} ms");
                latencies.Add(latencyInMs);

                watch.Reset();
            }

            latencies.Sort();
            Console.WriteLine($"\n\tp0:{latencies[0]}, p50: {latencies[(int)(numIterations * 0.50)]}, p90: {latencies[(int)(numIterations * 0.90)]}. p99: {latencies[(int)(numIterations * 0.99)]}");
            Console.WriteLine("\n");
            #endregion

            Console.WriteLine("Press any key to exit...");
            Console.ReadLine();
        }

        public string GetRandomString(int length)
        {
            Random random = new Random(System.Environment.TickCount);
            string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length).Select(s => s[random.Next(s.Length)]).ToArray());
        }

        public class CustomerEntity : TableEntity
        {
            public CustomerEntity(string lastName, string firstName)
            {
                this.PartitionKey = lastName;
                this.RowKey = firstName;
            }

            public CustomerEntity() { }

            public string Email { get; set; }

            public string PhoneNumber { get; set; }

            public string Bio { get; set; }
        }
    }
}

﻿//
// This is sample code from Christian Geuer-Pollmann (@chgeuer). Use it for whatever you like. 
//
namespace DemoHost
{
    using System;
    using System.IO;
    using System.Threading;

    using FFMPEGLib;
    using Microsoft.WindowsAzure.Storage;

    class DemoHostProgram
    {
        static void Main(string[] args)
        {
            string queueName = "chrisqueue";

            var cts = new CancellationTokenSource();
            var loop = new ExecutionLoop(
                cloudStorageAccount: CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")), 
                queueName: queueName, 
                visibilityTimeout: TimeSpan.FromMinutes(60), 
                logBlobStorage: "demologjobs", 
                tmpPath: new DirectoryInfo(Path.GetTempPath()),
                loggingFolder: new DirectoryInfo(".")
                );
            // loop.FlushQueue(cts.Token);

            //SampleJobs.SubmitSampleJob(Guid.NewGuid().ToString(), queueName);
            loop.Run(cts.Token);

            Console.WriteLine("run terminated");
            Console.ReadLine();
        }
    }
}
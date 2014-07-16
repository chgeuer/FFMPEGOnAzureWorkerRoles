namespace DemoHost
{
    using System;
    using System.IO;
    using System.Threading;

    using FFMPEGLib;
    using FFMPEGDemoClient;

    class DemoHostProgram
    {
        static void Main(string[] args)
        {
            string queueName = "chrisqueue";

            var cts = new CancellationTokenSource();
            var loop = new ExecutionLoop(
                cloudStorageAccount: SampleJobs.CloudStorageAccount, 
                queueName: queueName, 
                visibilityTimeout: TimeSpan.FromMinutes(60), 
                logBlobStorage: "demologjobs", 
                tmpPath: new DirectoryInfo(Path.GetTempPath()),
                loggingFolder: new DirectoryInfo(".")
                );
            // loop.FlushQueue(cts.Token);

            SampleJobs.SubmitSampleJob(Guid.NewGuid().ToString(), queueName);
            loop.Run(cts.Token);

            Console.WriteLine("run terminated");
            Console.ReadLine();
        }
    }
}
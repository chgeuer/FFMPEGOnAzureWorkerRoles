//
// This is sample code from Christian Geuer-Pollmann (@chgeuer). Use it for whatever you like. 
//
namespace FFMPEGDemoClient
{
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using System.IO;
    using FFMPEGLib;

    class FFMPEGDemoClientProgram
    {
        static void Main(string[] args)
        {
            var jobId = "gulliveotestdatasmalllogo";

            var client = SampleJobs.CloudStorageAccount.CreateCloudBlobClient();

            var policyName = "ffmpeg";

            var resultContainerName = "job-" + jobId;
            var resultContainer = client.GetContainerReference(resultContainerName);
            resultContainer.CreateIfNotExists();

            SampleJobs.SetContainerPolicy(client, resultContainerName, policyName);
            SampleJobs.SetContainerPolicy(client, "christiansampledata", policyName);
            SampleJobs.SetContainerPolicy(client, "christianinput", policyName);

            var job = SampleJobs.CreateJob(
                inputVideo: SampleJobs.GetSAS(client, "christiansampledata", "QuickTime-Testfile.mp4", policyName),
                watermarkUrl: SampleJobs.GetSAS(client, "christiansampledata", "logo_small.png", policyName),
                previewVideo: SampleJobs.GetSAS(client, resultContainerName, "preview.mp4", policyName),
                posterImage: SampleJobs.GetSAS(client, resultContainerName, "thumbnail.jpg", policyName),
                loggingBlob: SampleJobs.GetSAS(client, resultContainerName, "log.txt", policyName),
                jobCompletionUrl: "http://sampleapi.cloudapp.net/api/finishedstatus.php?job=123");

            var loop = new JobQueueWrapper(SampleJobs.CloudStorageAccount, "ffmpegqueue", TimeSpan.FromMinutes(60));
            loop.SubmitJob(job);

            Console.WriteLine(job.ToString());

            Console.WriteLine("\n\n\nJob submitted");
        }
    }
}
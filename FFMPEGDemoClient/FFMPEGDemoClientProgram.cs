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
    using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
    using Microsoft.WindowsAzure.Storage.Blob;

    class FFMPEGDemoClientProgram
    {
        public static Func<string, string> CreateSASGenerator(CloudStorageAccount account, string containername)
        {
            var client = account.CreateCloudBlobClient();
            var container = client.GetContainerReference(containername);
            container.CreateIfNotExists();

            #region set SAS Policy

            var policyName = "ffmpeg";
            var permissions = new BlobContainerPermissions { PublicAccess = BlobContainerPublicAccessType.Off };
            permissions.SharedAccessPolicies[policyName] = new SharedAccessBlobPolicy
            {
                Permissions = SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.Write | SharedAccessBlobPermissions.List,
                SharedAccessExpiryTime = DateTime.UtcNow.AddYears(1)
            };
            container.SetPermissions(permissions);

            #endregion

            return blobname =>
            {
                var blob = container.GetBlockBlobReference(blobname);
                var sas = blob.GetSharedAccessSignature(new SharedAccessBlobPolicy(), policyName);

                var fullUrl = blob.Uri.ToString() + sas;

                return fullUrl;
            };
        }


        static void Main(string[] args)
        {
            var jobId = Guid.NewGuid().ToString();
            var resultContainerName = "job-" + jobId;
            var account = SampleJobs.CloudStorageAccount;
            var client = account.CreateCloudBlobClient();

            var generateSAS = CreateSASGenerator(account, resultContainerName);

            Func<string, string, Task<bool>> blobExistsAsync = async (containerName, blobName) =>
            {
                var container = client
                    .GetContainerReference(containerName);
                try 
                {
                    var blob = await container.GetBlobReferenceFromServerAsync(blobName);
                    return await blob.ExistsAsync();
                } 
                catch (StorageException se) 
                {
                    return false;
                }
            };

            Func<string, Task> uploadAync = async fileName => 
            {
                var fi = new FileInfo(fileName);
                var exists = await blobExistsAsync(resultContainerName, fi.Name);
                if (!exists)
                {
                    await LargeFileUploader.LargeFileUploaderUtils.UploadAsync(fi, account, resultContainerName);
                }
            };

            Task.WaitAll(
                new [] { 
                    @"..\..\..\demodata\input.mp4", 
                    @"..\..\..\demodata\logo.png"
                }
                .Select(uploadAync)
                .ToArray());


            var job = SampleJobs.CreateJob(
                inputVideo: generateSAS("input.mp4"),
                watermarkUrl: generateSAS("logo.png"),
                previewVideo: generateSAS("preview.mp4"),
                posterImage: generateSAS("thumbnail.jpg"),
                loggingBlob: generateSAS("log.txt"),
                jobCompletionUrl: "http://sampleapi.cloudapp.net/api/finishedstatus.php?job=123");

            var loop = new JobQueueWrapper(SampleJobs.CloudStorageAccount, "ffmpegqueue", TimeSpan.FromMinutes(60));
            loop.SubmitJob(job);

            Console.WriteLine(job.ToString());

            Console.WriteLine("\n\n\nJob {0} submitted", jobId);
        }
    }
}
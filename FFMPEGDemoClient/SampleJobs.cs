//
// This is sample code from Christian Geuer-Pollmann (@chgeuer). Use it for whatever you like. 
//
namespace FFMPEGDemoClient
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Microsoft.WindowsAzure.Storage;
    using Microsoft.WindowsAzure.Storage.Auth;
    using Microsoft.WindowsAzure.Storage.Blob;
    using FFMPEGLib;

    public static class SampleJobs
    {
        public static CloudStorageAccount CloudStorageAccount
        {
            get
            {
                return CloudStorageAccount.DevelopmentStorageAccount;

                //return new CloudStorageAccount(
                //    new StorageCredentials(
                //        accountName: "storageaccount123",
                //        keyValue: "XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX=="),
                //    useHttps: true);
            }
        }
        
        public static VideoConversionJob CreateJob(string inputVideo, string watermarkUrl, string previewVideo, string posterImage, string loggingBlob, string jobCompletionUrl)
        {
            var resolution = @"-s ""512x288"" ";
            var videobitrate = @"-b:v 600k ";
            var watermark = @"-vf ""movie={{in:1}} [watermark]; [in][watermark] overlay=main_w-overlay_w-20:20 [out]"" ";
            var codec_mp4 = @"-vcodec libx264 -pix_fmt yuv420p " + watermark + videobitrate;
            var codec_poster = @"-ss 00:02 -vframes 1 -r 1 -f image2 " + watermark + videobitrate;
            var mp4_commandline = @"ffmpeg -i {{in:0}} " + codec_mp4 + resolution + @" {{out:0}} ";
            var poster_commandline = @"ffmpeg -i {{in:0}} " + codec_poster + resolution + @" {{out:1}} ";

            return new VideoConversionJob
            {
                InputArgs = new List<string> { inputVideo, watermarkUrl },
                ResultArgs = new List<string> { previewVideo, posterImage },
                FFMPEGCommandLines = new List<string> { poster_commandline, mp4_commandline},
                LoggingBlob = loggingBlob,
                JobCompletionNotificationUrl = jobCompletionUrl
            };
        }

        public static void SetContainerPolicy(CloudBlobClient client, string containername, string policyName)
        {
            var container = client.GetContainerReference(containername);

            container.CreateIfNotExists();

            var permissions = new BlobContainerPermissions { PublicAccess = BlobContainerPublicAccessType.Off };
            permissions.SharedAccessPolicies[policyName] = new SharedAccessBlobPolicy
            {
                Permissions = SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.Write | SharedAccessBlobPermissions.List,
                SharedAccessExpiryTime = DateTime.UtcNow.AddYears(1)
            };
            container.SetPermissions(permissions);
        }

        public static string GetSAS(CloudBlobClient client, string containername, string blobname, string policyName) 
        {
            var container = client.GetContainerReference(containername);
            var blob = container.GetBlockBlobReference(blobname);
            var sas = blob.GetSharedAccessSignature(new SharedAccessBlobPolicy(), policyName);

            return blob.Uri.ToString() + sas;
        }

        public static void Download()
        {
            var client = SampleJobs.CloudStorageAccount.CreateCloudBlobClient();

            var policyName = "ffmpeg";
            var containername = "christianinput"; 
            SetContainerPolicy(client, containername, policyName);
            var blobUri = GetSAS(client, containername, "input.mp4", policyName);

            // var blob = new CloudBlockBlob(new Uri(blobUri.Substring(0, blobUri.IndexOf("?"))), new StorageCredentials(blobUri.Substring(blobUri.IndexOf("?"))));
            var blob = new CloudBlockBlob(new Uri(blobUri));

            blob.DownloadToFile("1.mp4", FileMode.Create);
        }

        public static void SubmitSampleJob(string jobid, string queueName)
        {
            var client = SampleJobs.CloudStorageAccount.CreateCloudBlobClient();

            var policyName = "ffmpeg";

            var resultContainerName = "job" + jobid;
            Console.WriteLine("Result container: " + resultContainerName);
            var resultContainer = client.GetContainerReference(resultContainerName);
            resultContainer.CreateIfNotExists();
            SetContainerPolicy(client, resultContainerName, policyName);

            var inputcontainername = "christianinput";
            SetContainerPolicy(client, inputcontainername, policyName);

            var job = SampleJobs.CreateJob(
                inputVideo: SampleJobs.GetSAS(client, "christiansampledata", "QuickTime-Testfile.mp4", policyName),
                watermarkUrl: SampleJobs.GetSAS(client, "christiansampledata", "logo small.png", policyName),
                previewVideo: SampleJobs.GetSAS(client, resultContainerName, "preview.mp4", policyName),
                posterImage: SampleJobs.GetSAS(client, resultContainerName, "thumbnail.jpg", policyName),
                loggingBlob: SampleJobs.GetSAS(client, resultContainerName, "log.txt", policyName),
                jobCompletionUrl: string.Empty);

            var loop = new JobQueueWrapper(SampleJobs.CloudStorageAccount, queueName, TimeSpan.FromMinutes(60));
            loop.SubmitJob(job);
        }
    }
}
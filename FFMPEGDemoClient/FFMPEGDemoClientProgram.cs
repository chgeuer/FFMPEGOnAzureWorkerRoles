//
// This is sample code from Christian Geuer-Pollmann (@chgeuer). Use it for whatever you like. 
//
namespace FFMPEGDemoClient
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.WindowsAzure.Storage;
    using Microsoft.WindowsAzure.Storage.Blob;

    using FFMPEGLib;
    using Microsoft.WindowsAzure.Storage.Queue.Protocol;
    using Microsoft.WindowsAzure.Storage.Queue;
    using System.Collections.Generic;

    class FFMPEGDemoClientProgram
    {
        const string policyName = "ffmpeg";

        public static Func<string, string> CreateSASGenerator(CloudStorageAccount account, string containername)
        {
            var client = account.CreateCloudBlobClient();
            var container = client.GetContainerReference(containername);
            container.CreateIfNotExists();

            #region set SAS Policy

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

        static CloudQueue CreateResponseQueue(CloudStorageAccount account, string jobId, out string queueSAS)
        {
            var responseQueueClient = account.CreateCloudQueueClient();
            var responseQueue = responseQueueClient.GetQueueReference(jobId.ToLower());
            responseQueue.CreateIfNotExists();

            var queuePermissions = new QueuePermissions();
            queuePermissions.SharedAccessPolicies[policyName] = new SharedAccessQueuePolicy
            {
                Permissions = SharedAccessQueuePermissions.Add,
                SharedAccessExpiryTime = DateTime.UtcNow.AddYears(1)
            };
            responseQueue.SetPermissions(queuePermissions);
            queueSAS = responseQueue.Uri.ToString() + responseQueue.GetSharedAccessSignature(new SharedAccessQueuePolicy(), policyName);

            return responseQueue;
        }

        public static VideoConversionJob CreateJob(string inputVideo, string watermarkUrl, string previewVideo, string posterImage, string loggingBlob, string queueNotificationUrl)
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
                FFMPEGCommandLines = new List<string> { poster_commandline, mp4_commandline },
                LoggingBlob = loggingBlob,
                QueueNotificationUrl = queueNotificationUrl
            };
        }

        static void Main(string[] args)
        {
            Console.Write("Press <return> to run"); Console.ReadLine();


            var account = CloudStorageAccount.DevelopmentStorageAccount;

            var jobId = "job-" + Guid.NewGuid().ToString();
            var client = account.CreateCloudBlobClient();

            string queueSAS;
            var responseQueue = CreateResponseQueue(account, jobId: jobId, queueSAS: out queueSAS);

            var generateSAS = CreateSASGenerator(account, jobId);

            Func<string, string, Task<bool>> blobExistsAsync = async (containerName, blobName) =>
            {
                var container = client.GetContainerReference(containerName);
                try
                {
                    var blob = await container.GetBlobReferenceFromServerAsync(blobName);
                    return await blob.ExistsAsync();
                }
                catch (StorageException)
                {
                    return false;
                }
            };

            Func<string, Task> uploadAync = async fileName =>
            {
                var fi = new FileInfo(fileName);
                var exists = await blobExistsAsync(jobId, fi.Name);
                if (!exists)
                {
                    await LargeFileUploader.LargeFileUploaderUtils.UploadAsync(fi, account, jobId, 2);
                }
            };

            Task.WaitAll(
                new[] { 
                    @"..\..\..\demodata\input.mp4", 
                    @"..\..\..\demodata\logo.png"
                }
                .Select(uploadAync)
                .ToArray());


            var job = CreateJob(
                inputVideo: generateSAS("input.mp4"),
                watermarkUrl: generateSAS("logo.png"),
                previewVideo: generateSAS("preview.mp4"),
                posterImage: generateSAS("thumbnail.jpg"),
                loggingBlob: generateSAS("log.txt"),
                queueNotificationUrl: queueSAS);

            var loop = new JobQueueWrapper(account, "ffmpegqueue", TimeSpan.FromMinutes(60));
            loop.SubmitJob(job);

            Console.WriteLine(job.ToString());
            Console.WriteLine("\n\n\nJob {0} submitted", jobId);

            var msg = responseQueue.GetMessage();
            while (msg == null) { msg = responseQueue.GetMessage(); }


            Action<ConsoleColor, string> outstr = (c, s) =>
            {
                Console.ForegroundColor = c;
                Console.Out.WriteLine(s);
                Console.ResetColor();
            };

            var result = VideoConversionJobResults.FromJson(msg.AsString);
            outstr(ConsoleColor.Green, result.LoggerData);

            foreach (var r in result.Results)
            {
                outstr(ConsoleColor.Yellow, r.CommandLine);
                outstr(ConsoleColor.Blue, r.StandardOut);
                outstr(ConsoleColor.Red, r.StandardErr);
                outstr(ConsoleColor.White, r.ErrorCode.ToString());
            }

            responseQueue.Delete();
        }
    }
}
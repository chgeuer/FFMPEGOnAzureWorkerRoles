namespace FFMPEGLib
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.WindowsAzure.Storage;
    using Microsoft.WindowsAzure.Storage.Auth;
    using Microsoft.WindowsAzure.Storage.Queue;

    public class JobQueueWrapper
    {
        CloudQueue queue;
        public string QueueName { get; set; }

        public JobQueueWrapper(CloudStorageAccount c, string queueName, TimeSpan visibilityTimeout)
        {
            this.QueueName = queueName;
            var queueClient = c.CreateCloudQueueClient();
            this.VisibilityTimeout = visibilityTimeout;
            queue = queueClient.GetQueueReference(this.QueueName);
            queue.CreateIfNotExists();
        }

        public void SubmitJob(VideoConversionJob job)
        {
            var serializedJob = job.ToString();
            queue.AddMessage(new CloudQueueMessage(serializedJob));
        }

        public Tuple<VideoConversionJob, CloudQueueMessage> GetJob(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var message = queue.GetMessage(this.VisibilityTimeout);
                if (message != null)
                {
                    var job = VideoConversionJob.FromJson(message.AsString);
                    return new Tuple<VideoConversionJob, CloudQueueMessage>(job, message);
                }
            }

            return null;
        }

        public void Finished(Tuple<VideoConversionJob, CloudQueueMessage> jobData)
        {
            queue.DeleteMessage(jobData.Item2);
        }

        public TimeSpan VisibilityTimeout { get; set; }
    }
}

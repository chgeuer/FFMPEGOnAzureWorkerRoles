//
// This is sample code from Christian Geuer-Pollmann (@chgeuer). Use it for whatever you like. 
//
namespace FFMPEGLib
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;

    using Microsoft.WindowsAzure.Storage;
    using Microsoft.WindowsAzure.Storage.Blob;
    using Microsoft.WindowsAzure.Storage.Queue;


    public class ExecutionLoop
    {
        private readonly CloudStorageAccount cloudStorageAccount;
        private readonly string QueueName;
        private readonly TimeSpan VisibilityTimeout;
        private readonly string LogBlobStorage;
        // private readonly LogTable LogTable;
        private readonly DirectoryInfo TmpPath;
        private readonly DirectoryInfo LoggingFolder;

        public ExecutionLoop(CloudStorageAccount cloudStorageAccount, string queueName, 
            TimeSpan visibilityTimeout, string logBlobStorage, DirectoryInfo tmpPath, DirectoryInfo loggingFolder)
        {
            this.cloudStorageAccount = cloudStorageAccount;
            this.QueueName = queueName;
            this.VisibilityTimeout = visibilityTimeout;
            this.LogBlobStorage = logBlobStorage;
            // this.LogTable = new LogTable(this.cloudStorageAccount, "jobs");
            this.TmpPath = tmpPath;
            this.LoggingFolder = loggingFolder;
        }

        public void FlushQueue(CancellationToken ct)
        {
            var jobQueueWrapper = new JobQueueWrapper(this.cloudStorageAccount, this.QueueName, this.VisibilityTimeout);
            while (true)
            {
                var j = jobQueueWrapper.GetJob(ct);
                jobQueueWrapper.Finished(j);
            }
        }

        public void Run(CancellationToken ct)
        {
            var ffmpegPath = new FileInfo(@".\ffmpeg.exe").FullName;
            var regularDirectory = Environment.CurrentDirectory;
            var jobQueueWrapper = new JobQueueWrapper(this.cloudStorageAccount, this.QueueName, this.VisibilityTimeout);
            var blobClient = this.cloudStorageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference(this.LogBlobStorage);
            container.CreateIfNotExists();

            while (!ct.IsCancellationRequested)
            {
                var status = new Dictionary<string, string>();

                #region Create tempfolder

                var tmpFolder = new DirectoryInfo(Path.Combine(this.TmpPath.FullName, "ffmpeg" + Guid.NewGuid().ToString())).FullName;

                status["tmpFolder"] = tmpFolder;

                #endregion

                Tuple<VideoConversionJob, CloudQueueMessage> jobData = null;
                Logger logger = null;

                try
                {
                    jobData = jobQueueWrapper.GetJob(ct);
                    if (jobData == null) continue;

                    Directory.CreateDirectory(tmpFolder);
                    Environment.CurrentDirectory = tmpFolder;

                    var rowKey = DateTime.UtcNow.ToString("yyyyMMddHHmmssmmm");
                    var job = jobData.Item1;

                    logger = new Logger(job.LoggingBlob, this.LoggingFolder, job);
                    Action flush = () =>
                    {
                        logger.flushOutput();
                        status["output"] = logger.ToString();
                        // LogTable.Log(rowKey, status);
                    };

                    var jsonBlob = container.GetBlockBlobReference(string.Format("{0}.json", rowKey));
                    jsonBlob.UploadText(job.ToString());

                    status["json"] = job.ToString();
                    flush();
                    
                    // We sleep once before the polling, so that multiple workers starting at the same time don't collide. 
                    const int minSleepTimeMilliSeconds = 2500;
                    const int maxSleepTimeMilliSeconds = 6500;
                    Thread.Sleep(TimeSpan.FromMilliseconds(new Random().Next(minSleepTimeMilliSeconds, maxSleepTimeMilliSeconds)));

                    #region Download inputs

                    var localInputFiles = new List<FileInfo>();
                    for (int i = 0; i < job.InputArgs.Count; i++)
                    {
                        var fullurl = job.InputArgs[i];
                        var segments = new Uri(fullurl).Segments;
                        var input = segments[segments.Length - 1];
                        var localFile = new FileInfo(Path.Combine(tmpFolder, "in-" + i + input.Substring(input.LastIndexOf("."))));
                        var blob = new CloudBlockBlob(new Uri(fullurl));

                        logger.logInfo(string.Format("Downloading {0} to {1}", input, localFile));
                        flush();
                        blob.DownloadToFile(localFile.FullName, FileMode.CreateNew);

                        localInputFiles.Add(localFile);
                    }

                    #endregion

                    #region Determine result path

                    var localResultFiles = new List<FileInfo>();
                    for (int i = 0; i < job.ResultArgs.Count; i++)
                    {
                        var fullurl = job.ResultArgs[i];
                        var segments = new Uri(fullurl).Segments;
                        var resultFilename = segments[segments.Length - 1];
                        var localResultFile = new FileInfo(Path.Combine(tmpFolder, "out-" + i + resultFilename.Substring(resultFilename.LastIndexOf("."))));

                        localResultFiles.Add(localResultFile);
                    }

                    #endregion

                    #region Execute the damn thing and incrementally upload results

                    foreach (var commandlineAndFiles in job.FFMPEGCommandLines.Select(commandline => ExecutionLoop.SubstituteCommandlineArgs(commandline, localInputFiles, localResultFiles)))
                    {
                        var commandline = commandlineAndFiles.Item1;
                        var resultsOfThisExecution = commandlineAndFiles.Item2;

                        #region Execute

                        logger.logInfo(string.Format("Execute \"{0}\"", commandline));
                        flush();

                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo()
                            {
                                WorkingDirectory = tmpFolder,
                                FileName = ffmpegPath,
                                Arguments = commandline,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            },
                            EnableRaisingEvents = true
                        };

                        process.OutputDataReceived += (s, a) => logger.logInfo(a.Data);
                        process.ErrorDataReceived += (s, a) => logger.logErr(a.Data); 

                        try
                        {
                            process.Start();
                            process.BeginOutputReadLine();
                            process.BeginErrorReadLine();
                            process.WaitForExit();
                        }
                        catch (Exception ex)
                        {
                            logger.logErr(ex);
                            flush();

                            throw;
                        }

                        #endregion

                        #region Upload results

                        for (int i = 0; i < job.ResultArgs.Count; i++)
                        {
                            if (resultsOfThisExecution.Contains(localResultFiles[i]))
                            {
                                var blob = new CloudBlockBlob(new Uri(job.ResultArgs[i]));

                                logger.logInfo(string.Format("Upload {0} to {1}", localResultFiles[i].FullName, blob.Uri.AbsoluteUri));
                                flush();

                                blob.UploadFromFile(localResultFiles[i].FullName, FileMode.Open);
                            }
                        }

                        #endregion

                        if (process.ExitCode == 0)
                        {
                            logger.logInfo("ffmpeg ExitCode was OK");
                        }
                        else
                        {
                            logger.logErr("ffmpeg ExitCode indicated error: " + process.ExitCode);
                        }
                    }

                    #endregion

                    #region Call web site

                    if (!string.IsNullOrEmpty(job.JobCompletionNotificationUrl))
                    {
                        for (int i = 0; i < 5; i++)
                        {
                            var httpClient = new HttpClient();
                            var response = httpClient.GetAsync(job.JobCompletionNotificationUrl).Result;
                            if (response.IsSuccessStatusCode)
                            {
                                break;
                            }
                            else
                            {
                                logger.logErr(string.Format("Could not call {0}", job.JobCompletionNotificationUrl));
                                flush();
                                Thread.Sleep(TimeSpan.FromSeconds(2));
                            }

                        }
                    }

                    #endregion
                }
                catch (Exception exceptionWhileRunningJob)
                {
                    if (logger != null)
                    {
                        logger.logErr(exceptionWhileRunningJob);
                    }
                }
                finally
                {
                    try
                    {
                        if (jobData != null)
                        {
                            jobQueueWrapper.Finished(jobData);
                        }
                        if (logger != null)
                        {
                           logger.flushOutput();
                        }
                        Environment.CurrentDirectory = regularDirectory;
                        Directory.Delete(path: tmpFolder, recursive: true);
                    } 
                    catch (Exception ex)
                    {
                        Trace.TraceError(ex.Message);
                    }
                }
            }
        }

        public static Tuple<string,IList<FileInfo>> SubstituteCommandlineArgs(string _, IList<FileInfo> localInputFiles, IList<FileInfo> localResultFiles)
        {
            _ = Regex.Replace(_, "^ffmpeg[\\S]*", string.Empty);

            for (var i = 0; i < localInputFiles.Count; i++)
            {
                _ = _.Replace(string.Format("{{{{in:{0}}}}}", i), localInputFiles[i].Name);
            }
            for (var i = 0; i < localResultFiles.Count; i++)
            {
                _ = _.Replace(string.Format("{{{{out:{0}}}}}", i), localResultFiles[i].Name);
            }

            var missingArgs = new List<string>();
            for (var m = new Regex(@"({{(in|out):\d+}})").Match(_); m.Success; m = m.NextMatch())
            {
                missingArgs.Add(m.Value);
            }

            if (missingArgs.Count > 0)
            {
                Console.WriteLine("Command line specifies it needs a positional parameter, but the parameter is missing in the parameter list: ");
                foreach (var missingArg in missingArgs)
                {
                    Console.WriteLine("Missing {0}", missingArg);
                }
            }

            var outputsOfThisExecution = localResultFiles.Where(f => _.Contains(f.Name)).ToList();

            return new Tuple<string, IList<FileInfo>>(_, outputsOfThisExecution);
        }
    }
}
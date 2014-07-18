//
// This is sample code from Christian Geuer-Pollmann (@chgeuer). Use it for whatever you like. 
//
namespace FFMPEGLib
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Text;

    using Microsoft.WindowsAzure.Storage.Blob;

    public class Logger
    {
        public string Url { get; private set; }
        private object o = new object();
        private StringBuilder loggingOutput = new StringBuilder();
        private string LocalLogFile;

        public Logger(string url, DirectoryInfo logingFolder, VideoConversionJob job)
        {
            this.Url = url;

            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssmmm");

            File.WriteAllText(Path.Combine(logingFolder.FullName, timestamp + ".json"), job.ToString());

            this.LocalLogFile = Path.Combine(logingFolder.FullName, timestamp + ".txt");
            AppendLogFile(url);
            AppendLogFile(job.ToString());
            AppendLogFile("\n\n");
        }

        private void AppendLogFile(string s)
        {
            File.AppendAllLines(this.LocalLogFile, new[] { s }, Encoding.UTF8);
        }

        public void logInfo(string msg)
        {
            Trace.TraceInformation(msg);
            Console.Out.WriteLine(msg);

            lock (o)
            {
                loggingOutput.AppendLine("stdout: " + msg);
                AppendLogFile("stdout: " + msg);
            }
        }

        public void logErr(string msg)
        {
            Trace.TraceError(msg);
            Console.Error.WriteLine(msg);
            
            lock (o)
            {
                loggingOutput.AppendLine("sterr: " + msg);
                AppendLogFile("sterr: " + msg);
            }
        }

        public void logErr(Exception ex)
        {
            logErr("----------------------------------------");
            for (var e = ex; e != null; e = e.InnerException)
            {
                logErr(e.Message);
            }
            logErr(ex.StackTrace);
            flushOutput();
        }


        public void flushOutput()
        {
            if (this.Url != null)
            {
                try
                {
                    var blob = new CloudBlockBlob(new Uri(this.Url));

                    blob.UploadText(loggingOutput.ToString(), Encoding.UTF8);
                }
                catch (Exception) { }
            }
            else
            {
                Trace.TraceError("Cannot write logs to blob");
            }
        }

        public override string ToString()
        {
            return this.loggingOutput.ToString();
        }
    }
}
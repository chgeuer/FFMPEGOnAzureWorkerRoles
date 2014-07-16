//
// This is sample code from Christian Geuer-Pollmann (@chgeuer). Use it for whatever you like. 
//
namespace FFMPEGWorkerRole
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Net;
    using System.Threading;
    using Microsoft.WindowsAzure.Diagnostics;
    using Microsoft.WindowsAzure.ServiceRuntime;
    using Microsoft.WindowsAzure.Storage;
    using FFMPEGLib;

    public class WorkerRole : RoleEntryPoint
    {
        const string DiagnosticsConnectionString = "Microsoft.WindowsAzure.Plugins.Diagnostics.ConnectionString";

        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private ExecutionLoop loop;

        private readonly Action<string> info = s => Trace.TraceInformation(s);
        private readonly Action<string> warning = s => Trace.TraceWarning(s);
        private readonly Action<string> error = s => Trace.TraceError(s);

        public override bool OnStart()
        {

            var storageAccount = CloudStorageAccount.Parse(RoleEnvironment.GetConfigurationSettingValue("ApplicationStorageAccount"));
            var visibilityTimeout = TimeSpan.Parse(RoleEnvironment.GetConfigurationSettingValue("FFMPEGMaximumExecutionDuration"));
            var logBlobContainerName = RoleEnvironment.GetConfigurationSettingValue("JobLogBlobContainer");

            var tmpPath = new DirectoryInfo(@"C:\ffmpegtmp");
            if (!tmpPath.Exists)
            {
                tmpPath.Create();
            }

            var ffmpeglogs = new DirectoryInfo(@"C:\ffmpeglogs");
            if (!ffmpeglogs.Exists)
            {
                ffmpeglogs.Create();
            }

            this.loop = new ExecutionLoop(storageAccount, 
                RoleEnvironment.GetConfigurationSettingValue("QueueName"),
                visibilityTimeout, logBlobContainerName, tmpPath, ffmpeglogs);



            TimeSpan scheduledTransferPeriod = TimeSpan.FromMinutes(1);
            ServicePointManager.DefaultConnectionLimit = 12;
            var diagConfig = DiagnosticMonitor.GetDefaultInitialConfiguration();
            diagConfig.DiagnosticInfrastructureLogs.ScheduledTransferLogLevelFilter = Microsoft.WindowsAzure.Diagnostics.LogLevel.Verbose;
            diagConfig.DiagnosticInfrastructureLogs.ScheduledTransferPeriod = scheduledTransferPeriod;
            diagConfig.WindowsEventLog.ScheduledTransferLogLevelFilter = Microsoft.WindowsAzure.Diagnostics.LogLevel.Warning;
            diagConfig.WindowsEventLog.ScheduledTransferPeriod = scheduledTransferPeriod;
            diagConfig.Logs.ScheduledTransferLogLevelFilter = Microsoft.WindowsAzure.Diagnostics.LogLevel.Verbose;
            diagConfig.Logs.ScheduledTransferPeriod = scheduledTransferPeriod;
            diagConfig.Directories.DataSources.Add(new DirectoryConfiguration
            {
                Path = ffmpeglogs.FullName,
                Container = "ffmpeglogs"
            });
            diagConfig.ConfigurationChangePollInterval = TimeSpan.FromMinutes(1);
            DiagnosticMonitor.Start(DiagnosticsConnectionString, diagConfig);

            return base.OnStart();
        }

        public override void Run()
        {
            info("Run() called");

            this.loop.Run(this.cancellationTokenSource.Token);
        }

        public override void OnStop()
        {
            warning("OnStop() called, cancelling Tomcat");

            this.cancellationTokenSource.Cancel();
        }
    }
}
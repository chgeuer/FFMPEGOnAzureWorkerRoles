// ############################################################################
// #                                                                          #
// #        ---==>  T H I S  F I L E  I S   G E N E R A T E D  <==---         #
// #                                                                          #
// # This means that any edits to the .cs file will be lost when its          #
// # regenerated. Changes should instead be applied to the corresponding      #
// # text template file (.tt)                                                      #
// ############################################################################



// ############################################################################
// @@@ INCLUDING: https://github.com/chgeuer/AzureLargeFileUploader/raw/master/LargeFileUploaderUtils.cs
// ############################################################################
// Certains directives such as #define and // Resharper comments has to be 
// moved to top in order to work properly    
// ############################################################################
// ############################################################################

// ############################################################################
// @@@ BEGIN_INCLUDE: https://github.com/chgeuer/AzureLargeFileUploader/raw/master/LargeFileUploaderUtils.cs
namespace LargeFileUploader
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;

    using global::Microsoft.WindowsAzure.Storage;
    using global::Microsoft.WindowsAzure.Storage.Blob;

    public static class LargeFileUploaderUtils
    {
        const int kB = 1024;
        const int MB = kB * 1024;
        const long GB = MB * 1024;
        public static int NumBytesPerChunk = 4 * MB; // A block may be up to 4 MB in size. 
        public static Action<string> Log { get; set; }

        public static async Task UploadAsync(string inputFile, string storageConnectionString, string containerName, uint uploadParallelism = 1)
        {
            await new FileInfo(inputFile).UploadAsync(CloudStorageAccount.Parse(storageConnectionString), containerName, uploadParallelism);
        }

        public static async Task UploadAsync(this FileInfo file, CloudStorageAccount storageAccount, string containerName, uint uploadParallelism = 1)
        {
            const int MAXIMUM_UPLOAD_SIZE = 4 * MB;
            if (NumBytesPerChunk > MAXIMUM_UPLOAD_SIZE) { NumBytesPerChunk = MAXIMUM_UPLOAD_SIZE; }

            var blobName = file.Name;
            long fileLength = file.Length;

            var blobClient = storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference(containerName);
            await container.CreateIfNotExistsAsync();
            var blockBlob = container.GetBlockBlobReference(blobName);

            #region Which blocks exist in the file

            var allBlockInFile = Enumerable
                 .Range(0, 1 + ((int)(fileLength / NumBytesPerChunk)))
                 .Select(_ => new BlockMetadata(_, fileLength, NumBytesPerChunk))
                 .ToList();
            var blockIdList = allBlockInFile.Select(_ => _.BlockId).ToList();

            #endregion

            #region Which blocks are already uploaded

            List<BlockMetadata> missingBlocks = null;
            try
            {
                var existingBlocks = (await blockBlob.DownloadBlockListAsync(
                        BlockListingFilter.Uncommitted,
                        AccessCondition.GenerateEmptyCondition(),
                        new BlobRequestOptions { },
                        new OperationContext()))
                    .Where(_ => _.Length == NumBytesPerChunk)
                    .ToList();

                missingBlocks = allBlockInFile.Where(blockInFile => !existingBlocks.Any(existingBlock =>
                    existingBlock.Name == blockInFile.BlockId &&
                    existingBlock.Length == blockInFile.Length)).ToList();
            }
            catch (StorageException)
            {
                missingBlocks = allBlockInFile;
            }

            #endregion

            Func<BlockMetadata, Statistics, Task> uploadBlock = async (block, stats) =>
            {
                byte[] blockData = await GetFileContentAsync(file, block.Index, block.Length);
                string contentHash = md5()(blockData);

                DateTime start = DateTime.UtcNow;

                await ExecuteUntilSuccessAsync(async () =>
                {
                    await blockBlob.PutBlockAsync(block.BlockId, new MemoryStream(blockData, true), contentHash);
                }, consoleExceptionHandler);

                stats.Add(block.Length, start);
            };

            var s = new Statistics(missingBlocks.Sum(b => b.Length));

            await LargeFileUploaderUtils.ForEachAsync(
                source: missingBlocks,
                parallelUploads: 4,
                body: blockMetadata => uploadBlock(blockMetadata, s));

            await ExecuteUntilSuccessAsync(async () =>
            {
                await blockBlob.PutBlockListAsync(blockIdList);
            }, consoleExceptionHandler);

            log("PutBlockList succeeded, finished upload to {0}", blockBlob.Uri.AbsoluteUri);
        }

        internal static void log(string format, params object[] args)
        {
            if (Log != null) { Log(string.Format(format, args)); }
        }
        internal static async Task<byte[]> GetFileContentAsync(this FileInfo file, long offset, int length)
        {
            using (var stream = file.OpenRead())
            {
                stream.Seek(offset, SeekOrigin.Begin);

                byte[] contents = new byte[length];
                var len = await stream.ReadAsync(contents, 0, contents.Length);
                if (len == length)
                {
                    return contents;
                }

                byte[] rest = new byte[len];
                Array.Copy(contents, rest, len);
                return rest;
            }
        }
        internal static void consoleExceptionHandler(Exception ex)
        {
            log("Problem occured, trying again. Details of the problem: ");
            for (var e = ex; e != null; e = e.InnerException)
            {
                log(e.Message);
            }
            log("---------------------------------------------------------------------");
            log(ex.StackTrace);
            log("---------------------------------------------------------------------");
        }
        internal static async Task ExecuteUntilSuccessAsync(Func<Task> action, Action<Exception> exceptionHandler)
        {
            bool success = false;
            while (!success)
            {
                try
                {
                    await action();
                    success = true;
                }
                catch (Exception ex)
                {
                    if (exceptionHandler != null) { exceptionHandler(ex); }
                }
            }
        }
        internal static Task ForEachAsync<T>(this IEnumerable<T> source, int parallelUploads, Func<T, Task> body)
        {
            return Task.WhenAll(
                Partitioner
                .Create(source)
                .GetPartitions(parallelUploads)
                .Select(partition => Task.Run(async () =>
                {
                    using (partition)
                    {
                        while (partition.MoveNext())
                        {
                            await body(partition.Current);
                        }
                    }
                })));
        }
        internal static Func<byte[], string> md5()
        {
            var hashFunction = MD5.Create();

            return (content) => Convert.ToBase64String(hashFunction.ComputeHash(content));
        }
        internal class BlockMetadata
        {
            internal BlockMetadata(int id, long length, int bytesPerChunk)
            {
                this.Id = id;
                this.BlockId = Convert.ToBase64String(System.BitConverter.GetBytes(id));
                this.Index = ((long)id) * ((long)bytesPerChunk);
                long remainingBytesInFile = length - this.Index;
                this.Length = (int)Math.Min(remainingBytesInFile, (long)bytesPerChunk);
            }

            public long Index { get; private set; }
            public int Id { get; private set; }
            public string BlockId { get; private set; }
            public int Length { get; private set; }
        }
        internal class Statistics
        {
            public Statistics(long totalBytes) { this.TotalBytes = totalBytes; }

            internal readonly DateTime InitialStartTime = DateTime.UtcNow;
            
            internal readonly object _lock = new object();
            internal long TotalBytes { get; private set; }
            internal long Done { get; private set; }

            internal void Add(long moreBytes, DateTime start)
            {
                long done;
                lock (_lock)
                {
                    this.Done += moreBytes;
                    done = this.Done;
                }

                var kbPerSec = (((double)moreBytes) / (DateTime.UtcNow.Subtract(start).TotalSeconds * kB));
                var MBPerMin = (((double)moreBytes) / (DateTime.UtcNow.Subtract(start).TotalMinutes * MB));

                log(
                    "Uploaded {0} ({1}) with {2} kB/sec ({3} MB/min), {4}",
                    absoluteProgress(done, this.TotalBytes),
                    relativeProgress(done, this.TotalBytes),
                    kbPerSec.ToString("F0"),
                    MBPerMin.ToString("F1"),
                    estimatedArrivalTime()
                    );
            }

            internal string estimatedArrivalTime()
            {
                var now = DateTime.UtcNow;

                double elapsedSeconds = now.Subtract(InitialStartTime).TotalSeconds;
                double progress = ((double)this.Done) / ((double)this.TotalBytes);

                if (this.Done == 0) return "unknown time";

                double remainingSeconds = elapsedSeconds * (1 - progress) / progress;

                TimeSpan remaining = TimeSpan.FromSeconds(remainingSeconds);

                return string.Format("{0} remaining, (expect to finish by {1} local time)",
                    remaining.ToString("g"),
                    now.ToLocalTime().Add(remaining));
            }

            private static string absoluteProgress(long current, long total)
            {
                if (total < kB)
                {
                    // Bytes is reasonable
                    return string.Format("{0} of {1} bytes", current, total);
                }
                else if (total < 10 * MB)
                {
                    // kB is a reasonable unit
                    return string.Format("{0} of {1} kByte", (current / kB), (total / kB));
                }
                else if (total < 10 * GB)
                {
                    // MB is a reasonable unit
                    return string.Format("{0} of {1} MB", (current / MB), (total / MB));
                }
                else
                {
                    // GB is a reasonable unit
                    return string.Format("{0} of {1} GB", (current / GB), (total / GB));
                }
            }

            private static string relativeProgress(long current, long total)
            {
                return string.Format("{0} %",
                    (100.0 * current / total).ToString("F3"));
            }
        }
    }
}
// @@@ END_INCLUDE: https://github.com/chgeuer/AzureLargeFileUploader/raw/master/LargeFileUploaderUtils.cs
// ############################################################################

// ############################################################################
namespace notinuse.Include
{
    static partial class MetaData
    {
        public const string RootPath        = @"https://github.com/";
        public const string IncludeDate     = @"2014-07-17T11:39:55";

        public const string Include_0       = @"https://github.com/chgeuer/AzureLargeFileUploader/raw/master/LargeFileUploaderUtils.cs";
    }
}
// ############################################################################




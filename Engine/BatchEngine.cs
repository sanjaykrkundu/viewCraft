using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;

namespace ViewCraft.Engine
{
    public class BatchConvertOptions
    {
        public List<string> SourceFilePaths { get; set; } = new();
        public string OutputDirectory { get; set; } = string.Empty;
        public MagickFormat TargetFormat { get; set; } = MagickFormat.WebP;
        public int Quality { get; set; } = 90;
        public bool ResizeEnabled { get; set; } = false;
        public int MaxWidth { get; set; } = 1920;
        public int MaxHeight { get; set; } = 1080;
        public string Prefix { get; set; } = string.Empty;
        public string Suffix { get; set; } = string.Empty;
        public bool OverwriteExisting { get; set; } = false;
    }

    public class BatchProgress
    {
        public int Total { get; set; }
        public int Processed { get; set; }
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public string CurrentFile { get; set; } = string.Empty;
        public double Percentage => Total > 0 ? (Processed * 100.0) / Total : 0;
    }

    public static class BatchEngine
    {
        public static async Task RunBatchAsync(
            BatchConvertOptions options, 
            IProgress<BatchProgress>? progress = null, 
            CancellationToken ct = default)
        {
            if (options.SourceFilePaths.Count == 0) return;
            Directory.CreateDirectory(options.OutputDirectory);

            var batchProgress = new BatchProgress { Total = options.SourceFilePaths.Count };
            int succeededCount = 0;
            int failedCount = 0;
            int processedCount = 0;

            await Task.Run(() =>
            {
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount,
                    CancellationToken = ct
                };

                Parallel.ForEach(options.SourceFilePaths, parallelOptions, (sourcePath) =>
                {
                    if (ct.IsCancellationRequested) return;

                    string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
                    string targetExt = options.TargetFormat.ToString().ToLowerInvariant();
                    string newFileName = $"{options.Prefix}{fileNameWithoutExt}{options.Suffix}.{targetExt}";
                    string outputPath = Path.Combine(options.OutputDirectory, newFileName);

                    try
                    {
                        if (!File.Exists(outputPath) || options.OverwriteExisting)
                        {
                            using var image = new MagickImage(sourcePath);
                            image.AutoOrient();

                            if (options.ResizeEnabled && options.MaxWidth > 0 && options.MaxHeight > 0)
                            {
                                var size = new MagickGeometry((uint)options.MaxWidth, (uint)options.MaxHeight)
                                {
                                    IgnoreAspectRatio = false
                                };
                                image.Resize(size);
                            }

                            image.Quality = (uint)options.Quality;
                            image.Format = options.TargetFormat;
                            image.Write(outputPath);
                        }

                        Interlocked.Increment(ref succeededCount);
                    }
                    catch
                    {
                        Interlocked.Increment(ref failedCount);
                    }
                    finally
                    {
                        int done = Interlocked.Increment(ref processedCount);
                        lock (batchProgress)
                        {
                            batchProgress.Processed = done;
                            batchProgress.Succeeded = succeededCount;
                            batchProgress.Failed = failedCount;
                            batchProgress.CurrentFile = Path.GetFileName(sourcePath);
                        }
                        progress?.Report(batchProgress);
                    }
                });
            }, ct);
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Extensions;
using Ave.Extensions.FileSystem.Progress;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Samples
{
    /// <summary>
    /// Sample usage of move and copy operations with progress reporting.
    /// </summary>
    public static class MoveCopySamples
    {
        /// <summary>
        /// Example of copying a large file with progress reporting.
        /// </summary>
        public static async Task CopyFileWithProgressExample(IFileSystem fileSystem)
        {
            var source = CanonicalPath.Create("/source/largefile.bin").Value;
            var destination = CanonicalPath.Create("/dest/largefile.bin").Value;
            
            // Create progress reporter that updates console
            var progress = new Progress<ICopyProgress>(p =>
            {
                Console.WriteLine($"Copying: {p.CurrentFile}");
                Console.WriteLine($"Progress: {p.FilesProcessed}/{p.TotalFiles} files");
                Console.WriteLine($"Bytes: {p.BytesTransferred:N0}/{p.TotalBytes:N0} ({((CopyProgress)p).PercentComplete:F1}%)");
                Console.WriteLine();
            });
            
            var options = new CopyOptions
            {
                Overwrite = true,
                Progress = progress,
                BufferSize = 1024 * 1024 // 1MB buffer
            };
            
            var result = await fileSystem.CopyAsync(source, destination, options);
            
            if (result.IsSuccess)
            {
                Console.WriteLine("Copy completed successfully!");
            }
            else
            {
                Console.WriteLine($"Copy failed: {result.Error.Message}");
            }
        }
        
        /// <summary>
        /// Example of moving a directory with cancellation support.
        /// </summary>
        public static async Task MoveDirectoryWithCancellationExample(IFileSystem fileSystem)
        {
            var source = CanonicalPath.Create("/old-location/data").Value;
            var destination = CanonicalPath.Create("/new-location/data").Value;
            
            using var cts = new CancellationTokenSource();
            
            // Cancel after 30 seconds
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            
            var options = new MoveOptions
            {
                Overwrite = false,
                PreserveTimestamps = true,
                PreserveAttributes = true
            };
            
            try
            {
                var result = await fileSystem.MoveAsync(
                    source, 
                    destination, 
                    options, 
                    cts.Token);
                
                if (result.IsSuccess)
                {
                    Console.WriteLine("Move completed successfully!");
                }
                else
                {
                    Console.WriteLine($"Move failed: {result.Error.Message}");
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Move operation was cancelled.");
            }
        }
        
        /// <summary>
        /// Example of batch copying with overall progress.
        /// </summary>
        public static async Task BatchCopyWithProgressExample(IFileSystem fileSystem)
        {
            var filesToCopy = new[]
            {
                ("/source/file1.txt", "/dest/file1.txt"),
                ("/source/file2.txt", "/dest/file2.txt"),
                ("/source/file3.txt", "/dest/file3.txt")
            };
            
            var totalFiles = filesToCopy.Length;
            var filesCompleted = 0;
            
            var progress = new Progress<ICopyProgress>(p =>
            {
                // Update only when a file completes
                if (p.FilesProcessed > filesCompleted)
                {
                    filesCompleted = p.FilesProcessed;
                    var overallPercent = (double)filesCompleted / totalFiles * 100;
                    Console.WriteLine($"Overall progress: {filesCompleted}/{totalFiles} ({overallPercent:F1}%)");
                }
            });
            
            foreach (var (sourcePath, destPath) in filesToCopy)
            {
                var source = CanonicalPath.Create(sourcePath).Value;
                var dest = CanonicalPath.Create(destPath).Value;
                
                var options = new CopyOptions { Progress = progress };
                
                var result = await fileSystem.CopyAsync(source, dest, options);
                
                if (result.IsFailure)
                {
                    Console.WriteLine($"Failed to copy {sourcePath}: {result.Error.Message}");
                }
            }
            
            Console.WriteLine("Batch copy completed!");
        }
        
        /// <summary>
        /// Example showing directory copy with detailed progress.
        /// </summary>
        public static async Task CopyDirectoryWithDetailedProgressExample(IFileSystem fileSystem)
        {
            var source = CanonicalPath.Create("/source/projects").Value;
            var destination = CanonicalPath.Create("/backup/projects").Value;
            
            var lastReportTime = DateTime.UtcNow;
            var progress = new Progress<ICopyProgress>(p =>
            {
                var now = DateTime.UtcNow;
                // Report at most every 500ms to avoid console spam
                if (now - lastReportTime >= TimeSpan.FromMilliseconds(500))
                {
                    lastReportTime = now;
                    
                    var percent = ((CopyProgress)p).PercentComplete;
                    var currentFile = System.IO.Path.GetFileName(p.CurrentFile);
                    
                    Console.WriteLine($"[{percent:F1}%] Copying {currentFile} " +
                                    $"({p.FilesProcessed}/{p.TotalFiles} files, " +
                                    $"{p.BytesTransferred:N0}/{p.TotalBytes:N0} bytes)");
                }
            });
            
            var options = new CopyOptions
            {
                Progress = progress,
                Overwrite = false,
                PreserveTimestamps = true,
                PreserveAttributes = true
            };
            
            Console.WriteLine("Starting directory copy with progress reporting...");
            var result = await fileSystem.CopyAsync(source, destination, options);
            
            if (result.IsSuccess)
            {
                Console.WriteLine("\nDirectory copy completed successfully!");
            }
            else
            {
                Console.WriteLine($"\nDirectory copy failed: {result.Error.Message}");
            }
        }
    }
}
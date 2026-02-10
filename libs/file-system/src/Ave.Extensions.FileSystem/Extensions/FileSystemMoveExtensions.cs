using System;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Progress;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Extensions
{
    /// <summary>
    /// Extension methods for move operations.
    /// </summary>
    public static class FileSystemMoveExtensions
    {
        /// <summary>
        /// Moves a file or directory with options.
        /// </summary>
        public static async Task<Result<Unit, Error>> MoveAsync(
            this IFileSystem fileSystem,
            CanonicalPath source,
            CanonicalPath destination,
            MoveOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (fileSystem == null)
                throw new ArgumentNullException(nameof(fileSystem));
            
            options ??= MoveOptions.Default;
            
            // For now, delegate to the basic method
            // In a full implementation, we'd add progress reporting here
            var result = await fileSystem.MoveAsync(source, destination, options.Overwrite, cancellationToken);
            
            // Report completion if progress reporter is provided
            if (result.IsSuccess && options.Progress != null)
            {
                var progress = new CopyProgress(
                    bytesTransferred: 0, // Would need actual size
                    totalBytes: 0,
                    currentFile: destination.Value,
                    filesProcessed: 1,
                    totalFiles: 1);
                
                options.Progress.Report(progress);
            }
            
            return result;
        }
        
        /// <summary>
        /// Moves a file with progress reporting.
        /// </summary>
        public static async Task<Result<Unit, Error>> MoveFileWithProgressAsync(
            this IFileSystem fileSystem,
            CanonicalPath source,
            CanonicalPath destination,
            bool overwrite = false,
            IProgress<ICopyProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (fileSystem == null)
                throw new ArgumentNullException(nameof(fileSystem));
            
            // Get file size for progress reporting
            long fileSize = 0;
            if (progress != null)
            {
                // In real implementation, would get actual file size
                // For now, we'll use a placeholder
                fileSize = await GetFileSizeAsync(fileSystem, source, cancellationToken);
            }
            
            // Report start
            progress?.Report(new CopyProgress(0, fileSize, source.Value, 0, 1));
            
            // Perform the move
            var result = await fileSystem.MoveFileAsync(source, destination, overwrite, cancellationToken);
            
            // Report completion
            if (result.IsSuccess && progress != null)
            {
                progress.Report(new CopyProgress(fileSize, fileSize, destination.Value, 1, 1));
            }
            
            return result;
        }
        
        private static Task<long> GetFileSizeAsync(
            IFileSystem fileSystem, 
            CanonicalPath path, 
            CancellationToken cancellationToken)
        {
            // This is a simplified version
            // In real implementation, would use GetEntryAsync or similar
            try
            {
                // Placeholder - would need actual implementation
                return Task.FromResult(0L);
            }
            catch
            {
                return Task.FromResult(0L);
            }
        }
    }
}
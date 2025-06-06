using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.FileSystem.Traversal.Handlers;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Extension methods for file system operations.
    /// </summary>
    public static class FileSystemExtensions
    {
        /// <summary>
        /// Returns a read-only view of the file system.
        /// </summary>
        /// <param name="fileSystem">The file system to wrap.</param>
        /// <returns>A read-only file system wrapper.</returns>
        public static IReadOnlyFileSystem AsReadOnly(this IFileSystem fileSystem)
        {
            if (fileSystem is ReadOnlyFileSystemWrapper)
            {
                return fileSystem;
            }

            return new ReadOnlyFileSystemWrapper(fileSystem);
        }
        
        /// <summary>
        /// Returns the same read-only file system when already read-only.
        /// </summary>
        /// <param name="fileSystem">The read-only file system.</param>
        /// <returns>The same read-only file system instance.</returns>
        public static IReadOnlyFileSystem AsReadOnly(this IReadOnlyFileSystem fileSystem)
        {
            return fileSystem;
        }

        /// <summary>
        /// Gets a list of files in the specified directory.
        /// </summary>
        /// <param name="fileSystem">The file system instance.</param>
        /// <param name="path">The directory path to enumerate.</param>
        /// <param name="searchPattern">The search string to match against files.</param>
        /// <param name="recursive">Whether to search subdirectories recursively.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the file paths or an error.</returns>
        public static async Task<Result<IReadOnlyList<CanonicalPath>, FileSystemError>> GetFilesAsync(
            this IReadOnlyFileSystem fileSystem,
            CanonicalPath path,
            string searchPattern = "*",
            bool recursive = false,
            CancellationToken cancellationToken = default)
        {
            var handler = new PathCollectorHandler(searchPattern, includeFiles: true, includeDirectories: false);
            var options = new TraversalOptions(
                recursive: recursive,
                strategyHint: TraversalStrategyHint.Search);

            var result = await fileSystem.TraverseAsync(path, handler, options, cancellationToken);
            
            return result.OnSuccessMap(_ => handler.CollectedPaths);
        }

        /// <summary>
        /// Gets a list of directories in the specified directory.
        /// </summary>
        /// <param name="fileSystem">The file system instance.</param>
        /// <param name="path">The directory path to enumerate.</param>
        /// <param name="searchPattern">The search string to match against directories.</param>
        /// <param name="recursive">Whether to search subdirectories recursively.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the directory paths or an error.</returns>
        public static async Task<Result<IReadOnlyList<CanonicalPath>, FileSystemError>> GetDirectoriesAsync(
            this IReadOnlyFileSystem fileSystem,
            CanonicalPath path,
            string searchPattern = "*",
            bool recursive = false,
            CancellationToken cancellationToken = default)
        {
            var handler = new PathCollectorHandler(searchPattern, includeFiles: false, includeDirectories: true);
            var options = new TraversalOptions(
                recursive: recursive,
                strategyHint: TraversalStrategyHint.Search);

            var result = await fileSystem.TraverseAsync(path, handler, options, cancellationToken);
            
            return result.OnSuccessMap(_ => handler.CollectedPaths);
        }
    }
}
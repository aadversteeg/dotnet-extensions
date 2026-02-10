using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem.Mapping;
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
        public static async Task<Result<IReadOnlyList<CanonicalPath>, Error>> GetFilesAsync(
            this IReadOnlyFileSystem fileSystem,
            CanonicalPath path,
            string searchPattern = "*",
            bool recursive = false,
            CancellationToken cancellationToken = default)
        {
            var handler = new PathCollectorHandler<FileSystemEntry>(searchPattern, includeFiles: true, includeDirectories: false);
            var mapper = IdentityMapper.Instance;
            var options = new TraversalOptions(
                recursive: recursive,
                strategyHint: TraversalStrategyHint.Search);

            var result = await fileSystem.TraverseAsync(path, handler, mapper, options, cancellationToken);
            
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
        public static async Task<Result<IReadOnlyList<CanonicalPath>, Error>> GetDirectoriesAsync(
            this IReadOnlyFileSystem fileSystem,
            CanonicalPath path,
            string searchPattern = "*",
            bool recursive = false,
            CancellationToken cancellationToken = default)
        {
            var handler = new PathCollectorHandler<FileSystemEntry>(searchPattern, includeFiles: false, includeDirectories: true);
            var mapper = IdentityMapper.Instance;
            var options = new TraversalOptions(
                recursive: recursive,
                strategyHint: TraversalStrategyHint.Search);

            var result = await fileSystem.TraverseAsync(path, handler, mapper, options, cancellationToken);
            
            return result.OnSuccessMap(_ => handler.CollectedPaths);
        }

        /// <summary>
        /// Enumerates files as an async stream, yielding results as they're discovered.
        /// </summary>
        /// <param name="fileSystem">The file system instance.</param>
        /// <param name="path">The directory path to enumerate.</param>
        /// <param name="searchPattern">The search string to match against files.</param>
        /// <param name="recursive">Whether to search subdirectories recursively.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>An async enumerable of file paths.</returns>
        public static async IAsyncEnumerable<CanonicalPath> EnumerateFilesAsync(
            this IReadOnlyFileSystem fileSystem,
            CanonicalPath path,
            string searchPattern = "*",
            bool recursive = true,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var handler = new StreamingHandler<FileSystemEntry>(searchPattern, includeFiles: true, includeDirectories: false);
            var mapper = IdentityMapper.Instance;
            var options = new TraversalOptions(
                recursive: recursive,
                strategyHint: TraversalStrategyHint.MemoryEfficient);

            var traversalTask = fileSystem.TraverseAsync(path, handler, mapper, options, cancellationToken);

            await foreach (var item in handler.GetItemsAsync(cancellationToken))
            {
                yield return item;
            }

            var result = await traversalTask;
            if (result.IsFailure)
            {
                throw new System.IO.IOException($"Traversal failed: {result.Error.Message}");
            }
        }

        /// <summary>
        /// Enumerates directories as an async stream.
        /// </summary>
        /// <param name="fileSystem">The file system instance.</param>
        /// <param name="path">The directory path to enumerate.</param>
        /// <param name="searchPattern">The search string to match against directories.</param>
        /// <param name="recursive">Whether to search subdirectories recursively.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>An async enumerable of directory paths.</returns>
        public static async IAsyncEnumerable<CanonicalPath> EnumerateDirectoriesAsync(
            this IReadOnlyFileSystem fileSystem,
            CanonicalPath path,
            string searchPattern = "*",
            bool recursive = true,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var handler = new StreamingHandler<FileSystemEntry>(searchPattern, includeFiles: false, includeDirectories: true);
            var mapper = IdentityMapper.Instance;
            var options = new TraversalOptions(
                recursive: recursive,
                strategyHint: TraversalStrategyHint.MemoryEfficient);

            var traversalTask = fileSystem.TraverseAsync(path, handler, mapper, options, cancellationToken);

            await foreach (var item in handler.GetItemsAsync(cancellationToken))
            {
                yield return item;
            }

            var result = await traversalTask;
            if (result.IsFailure)
            {
                throw new System.IO.IOException($"Traversal failed: {result.Error.Message}");
            }
        }

        /// <summary>
        /// Enumerates file entries (with metadata) as an async stream.
        /// </summary>
        /// <param name="fileSystem">The file system instance.</param>
        /// <param name="path">The directory path to enumerate.</param>
        /// <param name="searchPattern">The search string to match against entries.</param>
        /// <param name="recursive">Whether to search subdirectories recursively.</param>
        /// <param name="includeFiles">Whether to include files in the results.</param>
        /// <param name="includeDirectories">Whether to include directories in the results.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>An async enumerable of file system entries.</returns>
        public static async IAsyncEnumerable<FileSystemEntry> EnumerateEntriesAsync(
            this IReadOnlyFileSystem fileSystem,
            CanonicalPath path,
            string searchPattern = "*",
            bool recursive = true,
            bool includeFiles = true,
            bool includeDirectories = true,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var handler = new StreamingEntryHandler<FileSystemEntry>(searchPattern, includeFiles, includeDirectories);
            var mapper = IdentityMapper.Instance;
            var options = new TraversalOptions(
                recursive: recursive,
                strategyHint: TraversalStrategyHint.MemoryEfficient);

            var traversalTask = fileSystem.TraverseAsync(path, handler, mapper, options, cancellationToken);

            await foreach (var entry in handler.GetEntriesAsync(cancellationToken))
            {
                yield return entry;
            }

            var result = await traversalTask;
            if (result.IsFailure)
            {
                throw new System.IO.IOException($"Traversal failed: {result.Error.Message}");
            }
        }
    }
}
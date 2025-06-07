using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Traversal.Strategies
{
    /// <summary>
    /// Base implementation for traversal strategies.
    /// </summary>
    public abstract class BaseTraversalStrategy : ITraversalStrategy
    {
        /// <summary>
        /// Traverses the file system starting from the specified entry.
        /// </summary>
        /// <typeparam name="TEntry">The type of entries to map and handle.</typeparam>
        /// <param name="entry">The entry to start traversal from.</param>
        /// <param name="handler">The handler to process each mapped entry.</param>
        /// <param name="mapper">The mapper to transform file system entries to TEntry.</param>
        /// <param name="options">The traversal options.</param>
        /// <param name="context">The current traversal context.</param>
        /// <param name="stats">The traversal statistics.</param>
        /// <param name="getChildren">Function to get children of a directory.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or failure.</returns>
        public abstract Task<Result<Unit, FileSystemError>> TraverseAsync<TEntry>(
            IFileSystemEntry entry,
            ITraversalHandler<TEntry> handler,
            IFileSystemEntryMapper<TEntry> mapper,
            TraversalOptions options,
            TraversalContext context,
            TraversalStats stats,
            Func<CanonicalPath, CancellationToken, Task<Result<IEnumerable<IFileSystemEntry>, FileSystemError>>> getChildren,
            CancellationToken cancellationToken);

        /// <summary>
        /// Determines whether an entry should be processed.
        /// All entries are processed by default - handlers control filtering.
        /// </summary>
        /// <param name="entry">The entry to check.</param>
        /// <param name="options">The traversal options.</param>
        /// <returns>Always true - handlers control filtering.</returns>
        protected bool ShouldProcessEntry(IFileSystemEntry entry, TraversalOptions options)
        {
            return true;
        }

        /// <summary>
        /// Determines whether a directory should be traversed based on options and control.
        /// </summary>
        /// <param name="context">The current traversal context.</param>
        /// <param name="options">The traversal options.</param>
        /// <param name="control">The traversal control.</param>
        /// <returns>true if the directory should be traversed; otherwise, false.</returns>
        protected bool ShouldTraverseDirectory(TraversalContext context, TraversalOptions options, TraversalControl control)
        {
            // Always traverse the immediate children of the starting directory (depth 0)
            // If not recursive, only traverse the starting directory's children
            if (!options.Recursive && context.CurrentDepth > 0)
                return false;

            if (options.MaxDepth.HasValue && context.CurrentDepth >= options.MaxDepth.Value)
                return false;

            if (control.MaxDepthFromHere.HasValue && 
                context.CurrentDepth >= context.CurrentDepth + control.MaxDepthFromHere.Value)
                return false;

            return control.Action == TraversalAction.Continue;
        }

        /// <summary>
        /// Maps a file system entry to the target type using the provided mapper.
        /// </summary>
        /// <typeparam name="TEntry">The type to map to.</typeparam>
        /// <param name="entry">The file system entry to map.</param>
        /// <param name="mapper">The mapper to use.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A result containing the mapped entry or an error.</returns>
        protected async Task<Result<TEntry, FileSystemError>> MapEntryAsync<TEntry>(
            IFileSystemEntry entry,
            IFileSystemEntryMapper<TEntry> mapper,
            CancellationToken cancellationToken)
        {
            try
            {
                return entry switch
                {
                    IFileEntry fileEntry => await mapper.MapFileAsync(fileEntry, CreateFileReader(fileEntry), cancellationToken),
                    IDirectoryEntry directoryEntry => await mapper.MapDirectoryAsync(directoryEntry, cancellationToken),
                    _ => Result<TEntry, FileSystemError>.Failure(new FileSystemError($"Unknown entry type: {entry.GetType().Name}"))
                };
            }
            catch (Exception ex)
            {
                return Result<TEntry, FileSystemError>.Failure(FileSystemError.FromException(ex, entry.Path.Value));
            }
        }

        /// <summary>
        /// Creates a file reader for the given file entry. This is a placeholder implementation.
        /// File system implementations should override this to provide appropriate file readers.
        /// </summary>
        /// <param name="fileEntry">The file entry to create a reader for.</param>
        /// <returns>A file reader instance.</returns>
        protected virtual IFileReader CreateFileReader(IFileEntry fileEntry)
        {
            throw new NotSupportedException("File reader creation must be implemented by file system implementations.");
        }
    }
}
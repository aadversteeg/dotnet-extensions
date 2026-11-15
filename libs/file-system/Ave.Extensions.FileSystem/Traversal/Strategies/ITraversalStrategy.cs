using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Traversal.Strategies
{
    /// <summary>
    /// Strategy for traversing file system entries.
    /// </summary>
    public interface ITraversalStrategy
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
        Task<Result<Unit, Error>> TraverseAsync<TEntry>(
            IFileSystemEntry entry,
            ITraversalHandler<TEntry> handler,
            IFileSystemEntryMapper<TEntry> mapper,
            TraversalOptions options,
            TraversalContext context,
            TraversalStats stats,
            Func<CanonicalPath, CancellationToken, Task<Result<IEnumerable<IFileSystemEntry>, Error>>> getChildren,
            CancellationToken cancellationToken);
    }
}
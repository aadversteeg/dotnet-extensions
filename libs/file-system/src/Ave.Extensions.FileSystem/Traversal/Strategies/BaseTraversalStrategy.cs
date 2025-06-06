using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
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
        /// <param name="entry">The entry to start traversal from.</param>
        /// <param name="handler">The handler to process each entry.</param>
        /// <param name="options">The traversal options.</param>
        /// <param name="context">The current traversal context.</param>
        /// <param name="stats">The traversal statistics.</param>
        /// <param name="getChildren">Function to get children of a directory.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or failure.</returns>
        public abstract Task<Result<Unit, FileSystemError>> TraverseAsync(
            FileSystemEntry entry,
            ITraversalHandler handler,
            TraversalOptions options,
            TraversalContext context,
            TraversalStats stats,
            Func<CanonicalPath, CancellationToken, Task<Result<IEnumerable<FileSystemEntry>, FileSystemError>>> getChildren,
            CancellationToken cancellationToken);

        /// <summary>
        /// Determines whether an entry should be processed.
        /// All entries are processed by default - handlers control filtering.
        /// </summary>
        /// <param name="entry">The entry to check.</param>
        /// <param name="options">The traversal options.</param>
        /// <returns>Always true - handlers control filtering.</returns>
        protected bool ShouldProcessEntry(FileSystemEntry entry, TraversalOptions options)
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
            if (!options.Recursive)
                return false;

            if (options.MaxDepth.HasValue && context.CurrentDepth >= options.MaxDepth.Value)
                return false;

            if (control.MaxDepthFromHere.HasValue && 
                context.CurrentDepth >= context.CurrentDepth + control.MaxDepthFromHere.Value)
                return false;

            return control.Action == TraversalAction.Continue;
        }
    }
}
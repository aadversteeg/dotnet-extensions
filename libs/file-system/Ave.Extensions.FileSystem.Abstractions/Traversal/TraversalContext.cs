using System;

namespace Ave.Extensions.FileSystem.Traversal
{
    /// <summary>
    /// Context information during traversal.
    /// </summary>
    public sealed class TraversalContext
    {
        /// <summary>
        /// Gets the root path where traversal started.
        /// </summary>
        public CanonicalPath RootPath { get; }
        
        /// <summary>
        /// Gets the current depth in the traversal.
        /// </summary>
        public int CurrentDepth { get; }
        
        /// <summary>
        /// Gets the current path being processed.
        /// </summary>
        public CanonicalPath CurrentPath { get; }
        
        /// <summary>
        /// Gets the number of items processed so far.
        /// </summary>
        public long ItemsProcessed { get; }
        
        /// <summary>
        /// Gets the UTC time when traversal started.
        /// </summary>
        public DateTime StartTimeUtc { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TraversalContext"/> class.
        /// </summary>
        /// <param name="rootPath">The root path where traversal started.</param>
        /// <param name="currentDepth">The current depth in the traversal.</param>
        /// <param name="currentPath">The current path being processed.</param>
        /// <param name="itemsProcessed">The number of items processed so far.</param>
        /// <param name="startTimeUtc">The UTC time when traversal started.</param>
        public TraversalContext(
            CanonicalPath rootPath,
            int currentDepth,
            CanonicalPath currentPath,
            long itemsProcessed,
            DateTime startTimeUtc)
        {
            RootPath = rootPath;
            CurrentDepth = currentDepth;
            CurrentPath = currentPath;
            ItemsProcessed = itemsProcessed;
            StartTimeUtc = startTimeUtc;
        }

        /// <summary>
        /// Creates an initial context for starting traversal.
        /// </summary>
        /// <param name="rootPath">The root path where traversal will start.</param>
        /// <returns>A new initial traversal context.</returns>
        public static TraversalContext CreateInitial(CanonicalPath rootPath) =>
            new TraversalContext(rootPath, 0, rootPath, 0, DateTime.UtcNow);

        /// <summary>
        /// Creates a new context for the next level of traversal.
        /// </summary>
        /// <param name="newPath">The new path for the next level.</param>
        /// <returns>A new traversal context for the next level.</returns>
        public TraversalContext NextLevel(CanonicalPath newPath) =>
            new TraversalContext(RootPath, CurrentDepth + 1, newPath, ItemsProcessed, StartTimeUtc);

        /// <summary>
        /// Creates a new context with incremented item count.
        /// </summary>
        /// <returns>A new traversal context with incremented item count.</returns>
        public TraversalContext IncrementItems() =>
            new TraversalContext(RootPath, CurrentDepth, CurrentPath, ItemsProcessed + 1, StartTimeUtc);

        /// <summary>
        /// Creates a new context with the specified path.
        /// </summary>
        /// <param name="newPath">The new path for the context.</param>
        /// <returns>A new traversal context with the specified path.</returns>
        public TraversalContext WithPath(CanonicalPath newPath) =>
            new TraversalContext(RootPath, CurrentDepth, newPath, ItemsProcessed, StartTimeUtc);
    }
}
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Traversal.Handlers
{
    /// <summary>
    /// Handler that collects file and directory paths during traversal.
    /// </summary>
    public sealed class PathCollectorHandler : ITraversalHandler
    {
        private readonly List<CanonicalPath> paths = new List<CanonicalPath>();
        private readonly bool includeFiles;
        private readonly bool includeDirectories;
        private readonly PatternMatcher matcher;

        /// <summary>
        /// Gets the collected paths.
        /// </summary>
        public IReadOnlyList<CanonicalPath> CollectedPaths => paths.AsReadOnly();

        /// <summary>
        /// Initializes a new instance of the <see cref="PathCollectorHandler"/> class.
        /// </summary>
        /// <param name="searchPattern">The search pattern for filtering entries.</param>
        /// <param name="includeFiles">Whether to include files in the collection.</param>
        /// <param name="includeDirectories">Whether to include directories in the collection.</param>
        public PathCollectorHandler(string searchPattern, bool includeFiles, bool includeDirectories)
        {
            this.includeFiles = includeFiles;
            this.includeDirectories = includeDirectories;
            this.matcher = new PatternMatcher(searchPattern);
        }

        /// <summary>
        /// Called before traversal begins.
        /// </summary>
        /// <param name="rootPath">The root path where traversal will begin.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success.</returns>
        public Task<Result<Unit, FileSystemError>> OnTraversalStartAsync(
            CanonicalPath rootPath, 
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
        }

        /// <summary>
        /// Handles a file entry during traversal.
        /// </summary>
        /// <param name="file">The file entry to handle.</param>
        /// <param name="context">The current traversal context.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing traversal control instructions.</returns>
        public Task<Result<TraversalControl, FileSystemError>> HandleFileAsync(
            FileSystemEntry file,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            if (includeFiles && matcher.IsMatch(file.Name))
            {
                paths.Add(file.Path);
            }
            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }

        /// <summary>
        /// Handles a directory entry during traversal.
        /// </summary>
        /// <param name="directory">The directory entry to handle.</param>
        /// <param name="context">The current traversal context.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing traversal control instructions.</returns>
        public Task<Result<TraversalControl, FileSystemError>> HandleDirectoryAsync(
            FileSystemEntry directory,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            if (includeDirectories && context.CurrentDepth > 0 && matcher.IsMatch(directory.Name))
            {
                paths.Add(directory.Path);
            }
            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }

        /// <summary>
        /// Called when traversal completes.
        /// </summary>
        /// <param name="summary">The summary of the traversal operation.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success.</returns>
        public Task<Result<Unit, FileSystemError>> OnTraversalCompleteAsync(
            TraversalSummary summary,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
        }
    }
}
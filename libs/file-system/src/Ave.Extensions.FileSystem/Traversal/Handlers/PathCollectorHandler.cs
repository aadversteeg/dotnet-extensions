using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Traversal.Handlers
{
    /// <summary>
    /// Handler that collects file and directory paths during traversal.
    /// </summary>
    /// <typeparam name="TEntry">The type of entries that must implement IFileSystemEntry.</typeparam>
    public sealed class PathCollectorHandler<TEntry> : ITraversalHandler<TEntry>
        where TEntry : IFileSystemEntry
    {
        private readonly List<CanonicalPath> _paths = [];
        private readonly bool _includeFiles;
        private readonly bool _includeDirectories;
        private readonly PatternMatcher _matcher;

        /// <summary>
        /// Gets the collected paths.
        /// </summary>
        public IReadOnlyList<CanonicalPath> CollectedPaths => _paths.AsReadOnly();

        /// <summary>
        /// Initializes a new instance of the <see cref="PathCollectorHandler{TEntry}"/> class.
        /// </summary>
        /// <param name="searchPattern">The search pattern for filtering entries.</param>
        /// <param name="includeFiles">Whether to include files in the collection.</param>
        /// <param name="includeDirectories">Whether to include directories in the collection.</param>
        public PathCollectorHandler(string searchPattern, bool includeFiles, bool includeDirectories)
        {
            _includeFiles = includeFiles;
            _includeDirectories = includeDirectories;
            _matcher = new PatternMatcher(searchPattern);
        }

        /// <summary>
        /// Called before traversal begins.
        /// </summary>
        /// <param name="rootPath">The root path where traversal will begin.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success.</returns>
        public Task<Result<Unit, Error>> OnTraversalStartAsync(
            CanonicalPath rootPath, 
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
        }

        /// <summary>
        /// Called when entering a directory before processing its contents.
        /// </summary>
        /// <param name="directory">The mapped directory entry being entered.</param>
        /// <param name="context">The current traversal context.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing traversal control instructions.</returns>
        public Task<Result<TraversalControl, Error>> OnDirectoryEnterAsync(
            TEntry directory,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }

        /// <summary>
        /// Handles a file entry during traversal.
        /// </summary>
        /// <param name="file">The mapped file entry to handle.</param>
        /// <param name="context">The current traversal context.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing traversal control instructions.</returns>
        public Task<Result<TraversalControl, Error>> OnFileAsync(
            TEntry file,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            if (_includeFiles && _matcher.IsMatch(file.Path.GetName()))
            {
                _paths.Add(file.Path);
            }
            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }

        /// <summary>
        /// Handles a directory entry during traversal.
        /// </summary>
        /// <param name="directory">The mapped directory entry to handle.</param>
        /// <param name="context">The current traversal context.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing traversal control instructions.</returns>
        public Task<Result<TraversalControl, Error>> OnDirectoryAsync(
            TEntry directory,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            if (_includeDirectories && context.CurrentDepth > 0 && _matcher.IsMatch(directory.Path.GetName()))
            {
                _paths.Add(directory.Path);
            }
            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }

        /// <summary>
        /// Called when traversal completes.
        /// </summary>
        /// <param name="summary">The summary of the traversal operation.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success.</returns>
        public Task<Result<Unit, Error>> OnTraversalCompleteAsync(
            TraversalSummary summary,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
        }
    }
}
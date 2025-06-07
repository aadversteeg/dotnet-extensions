using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Traversal.Internal
{
    /// <summary>
    /// Simple handler for collecting paths during traversal (internal use only).
    /// </summary>
    internal sealed class SimplePathCollectorHandler : ITraversalHandler
    {
        private readonly List<CanonicalPath> _paths = new List<CanonicalPath>();
        private readonly bool _includeFiles;
        private readonly bool _includeDirectories;
        private readonly Regex _regex;

        /// <summary>
        /// Gets the collected paths.
        /// </summary>
        public IReadOnlyList<CanonicalPath> CollectedPaths => _paths.AsReadOnly();

        /// <summary>
        /// Initializes a new instance of the <see cref="SimplePathCollectorHandler"/> class.
        /// </summary>
        /// <param name="searchPattern">The search pattern for filtering entries.</param>
        /// <param name="includeFiles">Whether to include files in the collection.</param>
        /// <param name="includeDirectories">Whether to include directories in the collection.</param>
        public SimplePathCollectorHandler(string searchPattern, bool includeFiles, bool includeDirectories)
        {
            _includeFiles = includeFiles;
            _includeDirectories = includeDirectories;
            
            // Convert glob pattern to regex
            var pattern = searchPattern ?? "*";
            var regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            _regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
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
            if (_includeFiles && IsMatch(file.Name))
            {
                _paths.Add(file.Path);
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
            if (_includeDirectories && context.CurrentDepth > 0 && IsMatch(directory.Name))
            {
                _paths.Add(directory.Path);
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

        private bool IsMatch(string input)
        {
            return _regex.IsMatch(input);
        }
    }
}
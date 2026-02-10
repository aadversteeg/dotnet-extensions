using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem.MimeDetective.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.MimeDetective.Traversal
{
    /// <summary>
    /// Traversal handler that filters files by content type.
    /// </summary>
    public class ContentTypeFilterHandler : ITraversalHandler<FileSystemEntryWithContentType>
    {
        private readonly string _targetContentType;
        private readonly List<FileSystemEntryWithContentType> _matches = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentTypeFilterHandler"/> class.
        /// </summary>
        /// <param name="targetContentType">The content type to filter for.</param>
        public ContentTypeFilterHandler(string targetContentType)
        {
            _targetContentType = targetContentType ?? throw new ArgumentNullException(nameof(targetContentType));
        }

        /// <summary>
        /// Gets the collected files that match the target content type.
        /// </summary>
        public IReadOnlyList<FileSystemEntryWithContentType> CollectedFiles => _matches;

        /// <summary>
        /// Called when traversal starts.
        /// </summary>
        /// <param name="rootPath">The root path being traversed.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or failure.</returns>
        public Task<Result<Unit, Error>> OnTraversalStartAsync(
            CanonicalPath rootPath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
        }

        /// <summary>
        /// Called when traversal completes.
        /// </summary>
        /// <param name="summary">The traversal summary.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or failure.</returns>
        public Task<Result<Unit, Error>> OnTraversalCompleteAsync(
            TraversalSummary summary,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
        }

        /// <summary>
        /// Called when entering a directory before processing its contents.
        /// </summary>
        /// <param name="directory">The directory entry being entered.</param>
        /// <param name="context">The current traversal context.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing traversal control instructions or an error.</returns>
        public Task<Result<TraversalControl, Error>> OnDirectoryEnterAsync(
            FileSystemEntryWithContentType directory,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }

        /// <summary>
        /// Handles a file entry and adds it to the collection if it matches the target content type.
        /// </summary>
        /// <param name="file">The file entry to handle.</param>
        /// <param name="context">The current traversal context.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing traversal control instructions or an error.</returns>
        public Task<Result<TraversalControl, Error>> OnFileAsync(
            FileSystemEntryWithContentType file,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            if (string.Equals(file.ContentType, _targetContentType, StringComparison.OrdinalIgnoreCase))
            {
                _matches.Add(file);
            }

            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }

        /// <summary>
        /// Handles a directory entry (directories are skipped for content type filtering).
        /// </summary>
        /// <param name="directory">The directory entry to handle.</param>
        /// <param name="context">The current traversal context.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing traversal control instructions or an error.</returns>
        public Task<Result<TraversalControl, Error>> OnDirectoryAsync(
            FileSystemEntryWithContentType directory,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            // Skip directories for content type filtering
            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }
    }
}
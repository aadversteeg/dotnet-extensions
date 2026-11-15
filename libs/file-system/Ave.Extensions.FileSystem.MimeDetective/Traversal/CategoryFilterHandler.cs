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
    /// File categories for filtering.
    /// </summary>
    public enum FileCategory
    {
        /// <summary>
        /// Image files (jpg, png, gif, etc.).
        /// </summary>
        Image,
        /// <summary>
        /// Document files (pdf, doc, txt, etc.).
        /// </summary>
        Document,
        /// <summary>
        /// Archive files (zip, rar, tar, etc.).
        /// </summary>
        Archive,
        /// <summary>
        /// Audio files (mp3, wav, flac, etc.).
        /// </summary>
        Audio,
        /// <summary>
        /// Video files (mp4, avi, mkv, etc.).
        /// </summary>
        Video,
        /// <summary>
        /// Text files (txt, csv, log, etc.).
        /// </summary>
        Text,
        /// <summary>
        /// Application files (exe, dll, etc.).
        /// </summary>
        Application
    }

    /// <summary>
    /// Traversal handler that filters files by category.
    /// </summary>
    public class CategoryFilterHandler : ITraversalHandler<FileSystemEntryWithContentType>
    {
        private readonly FileCategory _targetCategory;
        private readonly List<FileSystemEntryWithContentType> _matches = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="CategoryFilterHandler"/> class.
        /// </summary>
        /// <param name="targetCategory">The category to filter for.</param>
        public CategoryFilterHandler(FileCategory targetCategory)
        {
            _targetCategory = targetCategory;
        }

        /// <summary>
        /// Gets the collected files that match the target category.
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
        /// Handles a file entry and adds it to the collection if it matches the target category.
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
            if (BelongsToCategory(file.ContentType, _targetCategory))
            {
                _matches.Add(file);
            }

            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }

        /// <summary>
        /// Handles a directory entry (directories are skipped for category filtering).
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
            // Skip directories for category filtering
            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }

        /// <summary>
        /// Determines if a content type belongs to a specific category.
        /// </summary>
        /// <param name="contentType">The content type to check.</param>
        /// <param name="category">The category to check against.</param>
        /// <returns>True if the content type belongs to the category.</returns>
        public static bool BelongsToCategory(string? contentType, FileCategory category)
        {
            if (contentType is null || contentType.Length == 0)
                return false;

            return category switch
            {
                FileCategory.Image => contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase),
                FileCategory.Document => IsDocumentType(contentType),
                FileCategory.Archive => IsArchiveType(contentType),
                FileCategory.Audio => contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase),
                FileCategory.Video => contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase),
                FileCategory.Text => contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase),
                FileCategory.Application => contentType.StartsWith("application/", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        /// <summary>
        /// Determines if a content type is a document type.
        /// </summary>
        /// <param name="contentType">The content type to check.</param>
        /// <returns>True if the content type is a document type.</returns>
        private static bool IsDocumentType(string contentType)
        {
            return contentType.ToLowerInvariant() switch
            {
                "application/pdf" => true,
                "application/msword" => true,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => true,
                "application/vnd.ms-excel" => true,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => true,
                "application/vnd.ms-powerpoint" => true,
                "application/vnd.openxmlformats-officedocument.presentationml.presentation" => true,
                "application/rtf" => true,
                "text/plain" => true,
                "text/html" => true,
                "text/xml" => true,
                _ => false
            };
        }

        /// <summary>
        /// Determines if a content type is an archive type.
        /// </summary>
        /// <param name="contentType">The content type to check.</param>
        /// <returns>True if the content type is an archive type.</returns>
        private static bool IsArchiveType(string contentType)
        {
            return contentType.ToLowerInvariant() switch
            {
                "application/zip" => true,
                "application/x-rar-compressed" => true,
                "application/vnd.rar" => true,
                "application/x-7z-compressed" => true,
                "application/x-tar" => true,
                "application/gzip" => true,
                "application/x-gzip" => true,
                "application/x-bzip2" => true,
                _ => false
            };
        }
    }
}
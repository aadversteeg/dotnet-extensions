using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.MimeDetective.Configuration;
using Ave.Extensions.FileSystem.MimeDetective.Models;
using Ave.Extensions.FileSystem.MimeDetective.Services;
using Ave.Extensions.FileSystem.MimeDetective.Traversal;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.MimeDetective.Extensions
{
    /// <summary>
    /// Extension methods for file system operations with content type detection.
    /// </summary>
    public static class ContentTypeFileSystemExtensions
    {
        /// <summary>
        /// Gets a single file with content type information.
        /// </summary>
        /// <param name="fileSystem">The file system instance.</param>
        /// <param name="path">The path to the file.</param>
        /// <param name="options">Optional content type detection options.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the file entry with content type information or an error.</returns>
        public static Task<Result<FileSystemEntryWithContentType, Error>> GetFileWithContentTypeAsync(
            this IReadOnlyFileSystem fileSystem,
            CanonicalPath path,
            ContentTypeMapperOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var mapperFactory = ContentTypeMapperFactory.Default;
            var mapper = mapperFactory.CreateMapper(options ?? new ContentTypeMapperOptions());
            return fileSystem.GetEntryAsync(path, mapper, cancellationToken);
        }

        /// <summary>
        /// Gets all files with a specific content type.
        /// </summary>
        /// <param name="fileSystem">The file system instance.</param>
        /// <param name="path">The directory path to search.</param>
        /// <param name="contentType">The content type to filter for.</param>
        /// <param name="recursive">Whether to search subdirectories recursively.</param>
        /// <param name="options">Optional content type detection options.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the list of files with the specified content type or an error.</returns>
        public static async Task<Result<IReadOnlyList<FileSystemEntryWithContentType>, Error>>
            GetFilesByContentTypeAsync(
            this IReadOnlyFileSystem fileSystem,
            CanonicalPath path,
            string contentType,
            bool recursive = true,
            ContentTypeMapperOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var handler = new ContentTypeFilterHandler(contentType);
            var mapperFactory = ContentTypeMapperFactory.Default;
            var mapper = mapperFactory.CreateMapper(options ?? new ContentTypeMapperOptions());
            var traversalOptions = new TraversalOptions(recursive: recursive);

            var result = await fileSystem.TraverseAsync(path, handler, mapper, traversalOptions, cancellationToken);

            return result.OnSuccessMap(_ => handler.CollectedFiles);
        }

        /// <summary>
        /// Gets all files belonging to specific categories.
        /// </summary>
        /// <param name="fileSystem">The file system instance.</param>
        /// <param name="path">The directory path to search.</param>
        /// <param name="category">The category to filter for.</param>
        /// <param name="recursive">Whether to search subdirectories recursively.</param>
        /// <param name="options">Optional content type detection options.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the list of files in the specified category or an error.</returns>
        public static async Task<Result<IReadOnlyList<FileSystemEntryWithContentType>, Error>>
            GetFilesByCategoryAsync(
            this IReadOnlyFileSystem fileSystem,
            CanonicalPath path,
            FileCategory category,
            bool recursive = true,
            ContentTypeMapperOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var handler = new CategoryFilterHandler(category);
            var mapperFactory = ContentTypeMapperFactory.Default;
            var mapper = mapperFactory.CreateMapper(options ?? new ContentTypeMapperOptions());
            var traversalOptions = new TraversalOptions(recursive: recursive);

            var result = await fileSystem.TraverseAsync(path, handler, mapper, traversalOptions, cancellationToken);

            return result.OnSuccessMap(_ => handler.CollectedFiles);
        }

        /// <summary>
        /// Gets all image files in the specified directory.
        /// </summary>
        /// <param name="fileSystem">The file system instance.</param>
        /// <param name="path">The directory path to search.</param>
        /// <param name="recursive">Whether to search subdirectories recursively.</param>
        /// <param name="options">Optional content type detection options.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the list of image files or an error.</returns>
        public static Task<Result<IReadOnlyList<FileSystemEntryWithContentType>, Error>>
            GetImageFilesAsync(
            this IReadOnlyFileSystem fileSystem,
            CanonicalPath path,
            bool recursive = true,
            ContentTypeMapperOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return fileSystem.GetFilesByCategoryAsync(path, FileCategory.Image, recursive, options, cancellationToken);
        }

        /// <summary>
        /// Gets all document files in the specified directory.
        /// </summary>
        /// <param name="fileSystem">The file system instance.</param>
        /// <param name="path">The directory path to search.</param>
        /// <param name="recursive">Whether to search subdirectories recursively.</param>
        /// <param name="options">Optional content type detection options.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the list of document files or an error.</returns>
        public static Task<Result<IReadOnlyList<FileSystemEntryWithContentType>, Error>>
            GetDocumentFilesAsync(
            this IReadOnlyFileSystem fileSystem,
            CanonicalPath path,
            bool recursive = true,
            ContentTypeMapperOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return fileSystem.GetFilesByCategoryAsync(path, FileCategory.Document, recursive, options, cancellationToken);
        }

        /// <summary>
        /// Gets all archive files in the specified directory.
        /// </summary>
        /// <param name="fileSystem">The file system instance.</param>
        /// <param name="path">The directory path to search.</param>
        /// <param name="recursive">Whether to search subdirectories recursively.</param>
        /// <param name="options">Optional content type detection options.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the list of archive files or an error.</returns>
        public static Task<Result<IReadOnlyList<FileSystemEntryWithContentType>, Error>>
            GetArchiveFilesAsync(
            this IReadOnlyFileSystem fileSystem,
            CanonicalPath path,
            bool recursive = true,
            ContentTypeMapperOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return fileSystem.GetFilesByCategoryAsync(path, FileCategory.Archive, recursive, options, cancellationToken);
        }

        /// <summary>
        /// Filters an enumerable of files to only include those with the specified content type.
        /// </summary>
        /// <param name="files">The files to filter.</param>
        /// <param name="contentType">The content type to filter for.</param>
        /// <returns>An enumerable of files with the specified content type.</returns>
        public static IEnumerable<FileSystemEntryWithContentType> WithContentType(
            this IEnumerable<FileSystemEntryWithContentType> files,
            string contentType)
        {
            return files.Where(f => string.Equals(f.ContentType, contentType, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Filters an enumerable of files to only include those belonging to the specified category.
        /// </summary>
        /// <param name="files">The files to filter.</param>
        /// <param name="category">The category to filter for.</param>
        /// <returns>An enumerable of files in the specified category.</returns>
        public static IEnumerable<FileSystemEntryWithContentType> WithCategory(
            this IEnumerable<FileSystemEntryWithContentType> files,
            FileCategory category)
        {
            return files.Where(f => CategoryFilterHandler.BelongsToCategory(f.ContentType, category));
        }

        /// <summary>
        /// Filters an enumerable of files to only include those with a confidence level above the threshold.
        /// </summary>
        /// <param name="files">The files to filter.</param>
        /// <param name="minConfidence">The minimum confidence level (0.0 to 1.0).</param>
        /// <returns>An enumerable of files with confidence above the threshold.</returns>
        public static IEnumerable<FileSystemEntryWithContentType> WithMinConfidence(
            this IEnumerable<FileSystemEntryWithContentType> files,
            double minConfidence)
        {
            return files.Where(f => f.Confidence >= minConfidence);
        }
    }
}
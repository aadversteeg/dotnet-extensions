using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.FileSystem.MimeDetective.Configuration;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem.MimeDetective.Models;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;
using MimeDetective;

namespace Ave.Extensions.FileSystem.MimeDetective.Mapping
{
    /// <summary>
    /// Maps file system entries to entries with content type information.
    /// </summary>
    public class ContentTypeMapper : IFileSystemEntryMapper<FileSystemEntryWithContentType>
    {
        private readonly ContentInspector? _inspector;
        private readonly ContentTypeMapperOptions _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentTypeMapper"/> class.
        /// </summary>
        /// <param name="inspector">The content inspector for file signature detection.</param>
        /// <param name="options">Configuration options for content type mapping.</param>
        public ContentTypeMapper(
            ContentInspector? inspector,
            ContentTypeMapperOptions options)
        {
            _inspector = inspector;
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Maps a file entry to an entry with content type information.
        /// </summary>
        /// <param name="entry">The file entry to map.</param>
        /// <param name="fileReader">A file reader for accessing file content.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the mapped entry or an error.</returns>
        public async Task<Result<FileSystemEntryWithContentType, Error>> MapFileAsync(
            IFileEntry entry,
            IFileReader fileReader,
            CancellationToken cancellationToken = default)
        {
            try
            {
                string? contentType = null;
                double confidence = 0.0;

                // Try to detect content type if file has content and inspector is available
                if (entry.Info.Size > 0 && _inspector != null)
                {
                    var streamResult = await fileReader.ReadBytesAsync(_options.MaxBytesToRead, cancellationToken);
                    if (streamResult.IsSuccess)
                    {
                        var content = streamResult.Value;
                        var maxBytes = Math.Min(_options.MaxBytesToRead, content.Length);
                        var buffer = content.Take(maxBytes).ToArray();

                        try
                        {
                            // Convert to ImmutableArray for MimeDetective
                            var immutableBuffer = ImmutableArray.Create(buffer);
                            var results = _inspector.Inspect(immutableBuffer);
                            var bestMatch = results.FirstOrDefault();

                            if (bestMatch != null)
                            {
                                contentType = bestMatch.Definition.File.MimeType;
                                confidence = 1.0; // Simple confidence for now
                            }
                        }
                        catch
                        {
                            // If MimeDetective fails, fall back to extension-based detection
                        }
                    }
                }

                // Fallback to extension-based detection if needed
                if (string.IsNullOrEmpty(contentType) && 
                    _options.FallbackToExtension && 
                    entry.Info.Extension != null)
                {
                    contentType = GetContentTypeFromExtension(entry.Info.Extension);
                    confidence = 0.5; // Lower confidence for extension-based detection
                }

                // Build the extended entry
                var extendedEntry = new FileSystemEntryWithContentType(
                    entry.Path,
                    false, // Files are never directories
                    entry.Info.Size,
                    entry.Info.Extension,
                    entry.Info.Info.CreatedUtc,
                    entry.Info.Info.ModifiedUtc,
                    entry.Info.Info.LastAccessUtc,
                    entry.Info.Info.Attributes,
                    contentType ?? _options.DefaultContentType,
                    null, // File type description - can be added later
                    Array.Empty<string>(), // Alternative extensions - can be added later
                    confidence);

                return Result<FileSystemEntryWithContentType, Error>.Success(extendedEntry);
            }
            catch (Exception ex)
            {
                return Result<FileSystemEntryWithContentType, Error>.Failure(
                    new Error(FileSystemErrorCodes.Unknown, $"Content type detection failed: {ex.Message}"));
            }
        }

        /// <summary>
        /// Maps a directory entry to an entry with content type information.
        /// </summary>
        /// <param name="entry">The directory entry to map.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the mapped entry or an error.</returns>
        public Task<Result<FileSystemEntryWithContentType, Error>> MapDirectoryAsync(
            IDirectoryEntry entry,
            CancellationToken cancellationToken = default)
        {
            // Directories don't have content types
            var result = new FileSystemEntryWithContentType(
                entry.Path,
                true, // This is a directory
                null, // Directories don't have sizes
                null, // Directories don't have extensions
                entry.Info.Info.CreatedUtc,
                entry.Info.Info.ModifiedUtc,
                entry.Info.Info.LastAccessUtc,
                entry.Info.Info.Attributes,
                null, // No content type for directories
                null, // No file type description for directories
                Array.Empty<string>(), // No alternative extensions
                0.0); // No confidence

            return Task.FromResult(Result<FileSystemEntryWithContentType, Error>.Success(result));
        }

        /// <summary>
        /// Gets a content type based on file extension.
        /// </summary>
        /// <param name="extension">The file extension including the dot.</param>
        /// <returns>The content type or null if not found.</returns>
        private static string? GetContentTypeFromExtension(string extension)
        {
            // Simple extension to MIME type mapping
            return extension.ToLowerInvariant() switch
            {
                ".txt" => "text/plain",
                ".html" => "text/html",
                ".htm" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".pdf" => "application/pdf",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".ppt" => "application/vnd.ms-powerpoint",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                ".svg" => "image/svg+xml",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".mp4" => "video/mp4",
                ".avi" => "video/x-msvideo",
                ".zip" => "application/zip",
                ".rar" => "application/vnd.rar",
                ".7z" => "application/x-7z-compressed",
                ".tar" => "application/x-tar",
                ".gz" => "application/gzip",
                _ => null
            };
        }
    }
}
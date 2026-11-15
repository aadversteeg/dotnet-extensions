using System;
using System.Collections.Generic;
using System.IO;
using Ave.Extensions.FileSystem.Models;

namespace Ave.Extensions.FileSystem.MimeDetective.Models
{
    /// <summary>
    /// Represents a file system entry with content type detection information.
    /// </summary>
    public readonly record struct FileSystemEntryWithContentType : IFileSystemEntry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FileSystemEntryWithContentType"/> struct.
        /// </summary>
        /// <param name="path">The canonical path to the file system entry.</param>
        /// <param name="isDirectory">Whether this entry is a directory.</param>
        /// <param name="size">The size in bytes. Null for directories.</param>
        /// <param name="extension">The file extension including the dot. Null for directories.</param>
        /// <param name="createdUtc">The creation time in UTC.</param>
        /// <param name="modifiedUtc">The last modification time in UTC.</param>
        /// <param name="lastAccessUtc">The last access time in UTC.</param>
        /// <param name="attributes">The file system attributes.</param>
        /// <param name="contentType">The content type (MIME type) of the file.</param>
        /// <param name="fileTypeDescription">Human-readable description of the file type.</param>
        /// <param name="alternativeExtensions">All possible file extensions for this content type.</param>
        /// <param name="confidence">Confidence level of the detection (0.0 to 1.0).</param>
        public FileSystemEntryWithContentType(
            CanonicalPath path,
            bool isDirectory,
            long? size,
            string? extension,
            DateTime createdUtc,
            DateTime modifiedUtc,
            DateTime lastAccessUtc,
            FileAttributes attributes,
            string? contentType = null,
            string? fileTypeDescription = null,
            IReadOnlyList<string>? alternativeExtensions = null,
            double confidence = 0.0)
        {
            Path = path;
            IsDirectory = isDirectory;
            Size = size;
            Extension = extension;
            CreatedUtc = createdUtc;
            ModifiedUtc = modifiedUtc;
            LastAccessUtc = lastAccessUtc;
            Attributes = attributes;
            ContentType = contentType;
            FileTypeDescription = fileTypeDescription;
            AlternativeExtensions = alternativeExtensions ?? Array.Empty<string>();
            Confidence = confidence;
        }

        /// <summary>
        /// Gets the canonical path to the file system entry.
        /// </summary>
        public CanonicalPath Path { get; }

        /// <summary>
        /// Gets the name of the file system entry.
        /// </summary>
        public string Name => Path.GetName();

        /// <summary>
        /// Gets whether this entry is a directory.
        /// </summary>
        public bool IsDirectory { get; }

        /// <summary>
        /// Gets the size in bytes. Null for directories.
        /// </summary>
        public long? Size { get; }

        /// <summary>
        /// Gets the file extension including the dot. Null for directories.
        /// </summary>
        public string? Extension { get; }

        /// <summary>
        /// Gets the creation time in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; }

        /// <summary>
        /// Gets the last modification time in UTC.
        /// </summary>
        public DateTime ModifiedUtc { get; }

        /// <summary>
        /// Gets the last access time in UTC.
        /// </summary>
        public DateTime LastAccessUtc { get; }

        /// <summary>
        /// Gets the file system attributes.
        /// </summary>
        public FileAttributes Attributes { get; }

        /// <summary>
        /// Gets whether the entry is hidden.
        /// </summary>
        public bool IsHidden => Attributes.HasFlag(FileAttributes.Hidden);

        /// <summary>
        /// Gets whether the entry is a system entry.
        /// </summary>
        public bool IsSystem => Attributes.HasFlag(FileAttributes.System);

        /// <summary>
        /// Gets whether the entry is read-only.
        /// </summary>
        public bool IsReadOnly => Attributes.HasFlag(FileAttributes.ReadOnly);

        /// <summary>
        /// Gets the content type (MIME type) of the file, e.g., "application/pdf", "image/jpeg".
        /// </summary>
        public string? ContentType { get; }

        /// <summary>
        /// Gets the human-readable description of the file type.
        /// </summary>
        public string? FileTypeDescription { get; }

        /// <summary>
        /// Gets all possible file extensions for this content type.
        /// </summary>
        public IReadOnlyList<string> AlternativeExtensions { get; }

        /// <summary>
        /// Gets the confidence level of the detection (0.0 to 1.0).
        /// </summary>
        public double Confidence { get; }

        /// <summary>
        /// Creates a FileSystemEntryWithContentType from a base FileSystemEntry.
        /// </summary>
        /// <param name="entry">The base file system entry.</param>
        /// <param name="contentType">The content type (MIME type) of the file.</param>
        /// <param name="fileTypeDescription">Human-readable description of the file type.</param>
        /// <param name="alternativeExtensions">All possible file extensions for this content type.</param>
        /// <param name="confidence">Confidence level of the detection (0.0 to 1.0).</param>
        /// <returns>A new FileSystemEntryWithContentType instance.</returns>
        public static FileSystemEntryWithContentType FromEntry(
            FileSystemEntry entry,
            string? contentType = null,
            string? fileTypeDescription = null,
            IReadOnlyList<string>? alternativeExtensions = null,
            double confidence = 0.0)
        {
            return new FileSystemEntryWithContentType(
                entry.Path,
                entry.IsDirectory,
                entry.Size,
                entry.Extension,
                entry.CreatedUtc,
                entry.ModifiedUtc,
                entry.LastAccessUtc,
                entry.Attributes,
                contentType,
                fileTypeDescription,
                alternativeExtensions,
                confidence);
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;

namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Represents a file system entry (file or directory) with complete metadata.
    /// </summary>
    public readonly record struct FileSystemEntry : IFileSystemEntry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FileSystemEntry"/> class.
        /// </summary>
        /// <param name="path">The canonical path to the file system entry.</param>
        /// <param name="isDirectory">Whether this entry is a directory.</param>
        /// <param name="size">The size in bytes. Null for directories.</param>
        /// <param name="extension">The file extension including the dot. Null for directories.</param>
        /// <param name="createdUtc">The creation time in UTC.</param>
        /// <param name="modifiedUtc">The last modification time in UTC.</param>
        /// <param name="lastAccessUtc">The last access time in UTC.</param>
        /// <param name="attributes">The file system attributes.</param>
        /// <param name="metadata">Optional application-defined key-value metadata for this entry.</param>
        public FileSystemEntry(
            CanonicalPath path,
            bool isDirectory,
            long? size,
            string? extension,
            DateTime createdUtc,
            DateTime modifiedUtc,
            DateTime lastAccessUtc,
            FileAttributes attributes,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            Path = path;
            IsDirectory = isDirectory;
            Size = size;
            Extension = extension;
            CreatedUtc = createdUtc;
            ModifiedUtc = modifiedUtc;
            LastAccessUtc = lastAccessUtc;
            Attributes = attributes;
            Metadata = metadata;
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
        /// Gets optional application-defined key-value metadata for this entry.
        /// </summary>
        public IReadOnlyDictionary<string, string>? Metadata { get; }

    }
}
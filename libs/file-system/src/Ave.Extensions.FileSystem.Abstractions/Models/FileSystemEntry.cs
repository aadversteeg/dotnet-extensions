using System;
using System.IO;

namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Represents a file system entry (file or directory) with complete metadata.
    /// </summary>
    public class FileSystemEntry
    {
        /// <summary>
        /// Gets or sets the canonical path to the file system entry.
        /// </summary>
        public CanonicalPath Path { get; set; }

        /// <summary>
        /// Gets the name of the file system entry.
        /// </summary>
        public string Name => Path.GetName();

        /// <summary>
        /// Gets or sets whether this entry is a directory.
        /// </summary>
        public bool IsDirectory { get; set; }

        /// <summary>
        /// Gets or sets the size in bytes. Null for directories.
        /// </summary>
        public long? Size { get; set; }

        /// <summary>
        /// Gets or sets the file extension including the dot. Null for directories.
        /// </summary>
        public string? Extension { get; set; }

        /// <summary>
        /// Gets or sets the creation time in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>
        /// Gets or sets the last modification time in UTC.
        /// </summary>
        public DateTime ModifiedUtc { get; set; }

        /// <summary>
        /// Gets or sets the last access time in UTC.
        /// </summary>
        public DateTime LastAccessUtc { get; set; }

        /// <summary>
        /// Gets or sets the file system attributes.
        /// </summary>
        public FileAttributes Attributes { get; set; }

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
        /// Creates a FileSystemEntry from FileInfo with optional path mapping.
        /// </summary>
        /// <param name="fileInfo">The FileInfo to convert.</param>
        /// <param name="pathMapper">Optional path mapper for canonical path conversion.</param>
        /// <returns>A new FileSystemEntry instance.</returns>
        public static FileSystemEntry FromFileInfo(FileInfo fileInfo, IPathMapper? pathMapper = null)
        {
            if (fileInfo == null)
                throw new ArgumentNullException(nameof(fileInfo));

            CanonicalPath path;
            if (pathMapper != null)
            {
                var result = pathMapper.MapToCanonicalPath(fileInfo.FullName);
                if (result.IsFailure)
                    throw new ArgumentException($"Invalid file path: {fileInfo.FullName}", nameof(fileInfo));
                path = result.Value;
            }
            else
            {
                var result = CanonicalPath.Create(fileInfo.FullName.Replace('\\', '/'));
                if (result.IsFailure)
                    throw new ArgumentException($"Invalid file path: {fileInfo.FullName}", nameof(fileInfo));
                path = result.Value;
            }

            return new FileSystemEntry
            {
                Path = path,
                IsDirectory = false,
                Size = fileInfo.Length,
                Extension = fileInfo.Extension,
                CreatedUtc = fileInfo.CreationTimeUtc,
                ModifiedUtc = fileInfo.LastWriteTimeUtc,
                LastAccessUtc = fileInfo.LastAccessTimeUtc,
                Attributes = fileInfo.Attributes
            };
        }

        /// <summary>
        /// Creates a FileSystemEntry from DirectoryInfo with optional path mapping.
        /// </summary>
        /// <param name="directoryInfo">The DirectoryInfo to convert.</param>
        /// <param name="pathMapper">Optional path mapper for canonical path conversion.</param>
        /// <returns>A new FileSystemEntry instance.</returns>
        public static FileSystemEntry FromDirectoryInfo(DirectoryInfo directoryInfo, IPathMapper? pathMapper = null)
        {
            if (directoryInfo == null)
                throw new ArgumentNullException(nameof(directoryInfo));

            CanonicalPath path;
            if (pathMapper != null)
            {
                var result = pathMapper.MapToCanonicalPath(directoryInfo.FullName);
                if (result.IsFailure)
                    throw new ArgumentException($"Invalid directory path: {directoryInfo.FullName}", nameof(directoryInfo));
                path = result.Value;
            }
            else
            {
                var result = CanonicalPath.Create(directoryInfo.FullName.Replace('\\', '/'));
                if (result.IsFailure)
                    throw new ArgumentException($"Invalid directory path: {directoryInfo.FullName}", nameof(directoryInfo));
                path = result.Value;
            }

            return new FileSystemEntry
            {
                Path = path,
                IsDirectory = true,
                Size = null,
                Extension = null,
                CreatedUtc = directoryInfo.CreationTimeUtc,
                ModifiedUtc = directoryInfo.LastWriteTimeUtc,
                LastAccessUtc = directoryInfo.LastAccessTimeUtc,
                Attributes = directoryInfo.Attributes
            };
        }
    }
}
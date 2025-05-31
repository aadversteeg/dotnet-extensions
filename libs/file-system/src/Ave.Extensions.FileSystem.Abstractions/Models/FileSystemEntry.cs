using System;
using System.IO;

namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Represents a file system entry (file or directory) with basic information.
    /// </summary>
    public class FileSystemEntry
    {
        /// <summary>
        /// Gets or sets the full path to the file system entry.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the name of the file system entry.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether this entry is a directory.
        /// </summary>
        public bool IsDirectory { get; set; }

        /// <summary>
        /// Gets or sets the size of the entry in bytes. Only applicable for files.
        /// </summary>
        public long? Size { get; set; }

        /// <summary>
        /// Gets or sets the last modification time in UTC.
        /// </summary>
        public DateTime ModifiedUtc { get; set; }

        /// <summary>
        /// Gets or sets the file system attributes.
        /// </summary>
        public FileAttributes Attributes { get; set; }

        /// <summary>
        /// Gets a value indicating whether the entry is hidden.
        /// </summary>
        public bool IsHidden => Attributes.HasFlag(FileAttributes.Hidden);

        /// <summary>
        /// Gets a value indicating whether the entry is a system entry.
        /// </summary>
        public bool IsSystem => Attributes.HasFlag(FileAttributes.System);

        /// <summary>
        /// Gets a value indicating whether the entry is read-only.
        /// </summary>
        public bool IsReadOnly => Attributes.HasFlag(FileAttributes.ReadOnly);

        /// <summary>
        /// Creates a FileSystemEntry from a FileInfo.
        /// </summary>
        /// <param name="fileInfo">The FileInfo to convert.</param>
        /// <returns>A new FileSystemEntry instance.</returns>
        public static FileSystemEntry FromFileInfo(FileInfo fileInfo)
        {
            if (fileInfo == null)
                throw new ArgumentNullException(nameof(fileInfo));

            return new FileSystemEntry
            {
                Path = fileInfo.FullName,
                Name = fileInfo.Name,
                IsDirectory = false,
                Size = fileInfo.Length,
                ModifiedUtc = fileInfo.LastWriteTimeUtc,
                Attributes = fileInfo.Attributes
            };
        }

        /// <summary>
        /// Creates a FileSystemEntry from a DirectoryInfo.
        /// </summary>
        /// <param name="directoryInfo">The DirectoryInfo to convert.</param>
        /// <returns>A new FileSystemEntry instance.</returns>
        public static FileSystemEntry FromDirectoryInfo(DirectoryInfo directoryInfo)
        {
            if (directoryInfo == null)
                throw new ArgumentNullException(nameof(directoryInfo));

            return new FileSystemEntry
            {
                Path = directoryInfo.FullName,
                Name = directoryInfo.Name,
                IsDirectory = true,
                Size = null,
                ModifiedUtc = directoryInfo.LastWriteTimeUtc,
                Attributes = directoryInfo.Attributes
            };
        }
    }
}
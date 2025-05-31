using System;
using System.IO;

namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Represents metadata information about a file.
    /// </summary>
    public class FileMetadata
    {
        /// <summary>
        /// Gets or sets the full path to the file.
        /// </summary>
        public string FullPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the name of the file including extension.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the file extension (including the dot).
        /// </summary>
        public string Extension { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the size of the file in bytes.
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// Gets or sets the creation time of the file in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>
        /// Gets or sets the last modification time of the file in UTC.
        /// </summary>
        public DateTime ModifiedUtc { get; set; }

        /// <summary>
        /// Gets or sets the last access time of the file in UTC.
        /// </summary>
        public DateTime LastAccessUtc { get; set; }

        /// <summary>
        /// Gets or sets the file attributes.
        /// </summary>
        public FileAttributes Attributes { get; set; }

        /// <summary>
        /// Gets a value indicating whether the file is read-only.
        /// </summary>
        public bool IsReadOnly => Attributes.HasFlag(FileAttributes.ReadOnly);

        /// <summary>
        /// Gets a value indicating whether the file is hidden.
        /// </summary>
        public bool IsHidden => Attributes.HasFlag(FileAttributes.Hidden);

        /// <summary>
        /// Gets a value indicating whether the file is a system file.
        /// </summary>
        public bool IsSystem => Attributes.HasFlag(FileAttributes.System);

        /// <summary>
        /// Creates a FileMetadata instance from a FileInfo object.
        /// </summary>
        /// <param name="fileInfo">The FileInfo to convert.</param>
        /// <returns>A new FileMetadata instance.</returns>
        public static FileMetadata FromFileInfo(FileInfo fileInfo)
        {
            if (fileInfo == null)
                throw new ArgumentNullException(nameof(fileInfo));

            return new FileMetadata
            {
                FullPath = fileInfo.FullName,
                Name = fileInfo.Name,
                Extension = fileInfo.Extension,
                Size = fileInfo.Length,
                CreatedUtc = fileInfo.CreationTimeUtc,
                ModifiedUtc = fileInfo.LastWriteTimeUtc,
                LastAccessUtc = fileInfo.LastAccessTimeUtc,
                Attributes = fileInfo.Attributes
            };
        }
    }
}
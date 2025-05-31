using System;
using System.IO;

namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Represents metadata information about a directory.
    /// </summary>
    public class DirectoryMetadata
    {
        /// <summary>
        /// Gets or sets the full path to the directory.
        /// </summary>
        public string FullPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the name of the directory.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the creation time of the directory in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>
        /// Gets or sets the last modification time of the directory in UTC.
        /// </summary>
        public DateTime ModifiedUtc { get; set; }

        /// <summary>
        /// Gets or sets the last access time of the directory in UTC.
        /// </summary>
        public DateTime LastAccessUtc { get; set; }

        /// <summary>
        /// Gets or sets the directory attributes.
        /// </summary>
        public FileAttributes Attributes { get; set; }

        /// <summary>
        /// Gets a value indicating whether the directory is hidden.
        /// </summary>
        public bool IsHidden => Attributes.HasFlag(FileAttributes.Hidden);

        /// <summary>
        /// Gets a value indicating whether the directory is a system directory.
        /// </summary>
        public bool IsSystem => Attributes.HasFlag(FileAttributes.System);

        /// <summary>
        /// Creates a DirectoryMetadata instance from a DirectoryInfo object.
        /// </summary>
        /// <param name="directoryInfo">The DirectoryInfo to convert.</param>
        /// <returns>A new DirectoryMetadata instance.</returns>
        public static DirectoryMetadata FromDirectoryInfo(DirectoryInfo directoryInfo)
        {
            if (directoryInfo == null)
                throw new ArgumentNullException(nameof(directoryInfo));

            return new DirectoryMetadata
            {
                FullPath = directoryInfo.FullName,
                Name = directoryInfo.Name,
                CreatedUtc = directoryInfo.CreationTimeUtc,
                ModifiedUtc = directoryInfo.LastWriteTimeUtc,
                LastAccessUtc = directoryInfo.LastAccessTimeUtc,
                Attributes = directoryInfo.Attributes
            };
        }
    }
}
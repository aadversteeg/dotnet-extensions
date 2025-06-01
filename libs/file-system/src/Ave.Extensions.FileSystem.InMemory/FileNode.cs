using System;
using System.IO;

namespace Ave.Extensions.FileSystem.InMemory
{
    /// <summary>
    /// Represents a file node in the in-memory file system.
    /// </summary>
    internal class FileNode
    {
        /// <summary>
        /// Initializes a new instance of the FileNode class.
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <param name="content">The file content.</param>
        /// <param name="createdUtc">The creation time.</param>
        public FileNode(CanonicalPath path, byte[] content, DateTime createdUtc)
        {
            Path = path;
            Content = content ?? throw new ArgumentNullException(nameof(content));
            CreatedUtc = createdUtc;
            ModifiedUtc = createdUtc;
            LastAccessUtc = createdUtc;
            Attributes = FileAttributes.Normal;
        }

        /// <summary>
        /// Gets the file path.
        /// </summary>
        public CanonicalPath Path { get; }

        /// <summary>
        /// Gets or sets the file content.
        /// </summary>
        public byte[] Content { get; set; }

        /// <summary>
        /// Gets the creation time in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; }

        /// <summary>
        /// Gets or sets the last modified time in UTC.
        /// </summary>
        public DateTime ModifiedUtc { get; set; }

        /// <summary>
        /// Gets or sets the last access time in UTC.
        /// </summary>
        public DateTime LastAccessUtc { get; set; }

        /// <summary>
        /// Gets or sets the file attributes.
        /// </summary>
        public FileAttributes Attributes { get; set; }
    }
}
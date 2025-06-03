using System;
using System.IO;

namespace Ave.Extensions.FileSystem.InMemory
{
    /// <summary>
    /// Represents a directory node in the in-memory file system.
    /// </summary>
    internal class DirectoryNode
    {
        /// <summary>
        /// Initializes a new instance of the DirectoryNode class.
        /// </summary>
        /// <param name="path">The directory path.</param>
        /// <param name="createdUtc">The creation time.</param>
        public DirectoryNode(CanonicalPath path, DateTime createdUtc)
        {
            Path = path;
            CreatedUtc = createdUtc;
            ModifiedUtc = createdUtc;
            LastAccessUtc = createdUtc;
            Attributes = FileAttributes.Directory;
        }

        /// <summary>
        /// Gets the directory path.
        /// </summary>
        public CanonicalPath Path { get; }

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
        /// Gets or sets the directory attributes.
        /// </summary>
        public FileAttributes Attributes { get; set; }
    }
}
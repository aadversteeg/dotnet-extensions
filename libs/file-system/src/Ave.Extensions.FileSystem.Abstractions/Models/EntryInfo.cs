using System;
using System.IO;

namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Common metadata for file system entries.
    /// </summary>
    public readonly struct EntryInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EntryInfo"/> struct.
        /// </summary>
        /// <param name="name">The name of the entry.</param>
        /// <param name="createdUtc">The creation time in UTC.</param>
        /// <param name="modifiedUtc">The last modification time in UTC.</param>
        /// <param name="lastAccessUtc">The last access time in UTC.</param>
        /// <param name="attributes">The file system attributes.</param>
        public EntryInfo(
            string name,
            DateTime createdUtc,
            DateTime modifiedUtc,
            DateTime lastAccessUtc,
            FileAttributes attributes)
        {
            Name = name;
            CreatedUtc = createdUtc;
            ModifiedUtc = modifiedUtc;
            LastAccessUtc = lastAccessUtc;
            Attributes = attributes;
        }

        /// <summary>
        /// Gets the name of the entry.
        /// </summary>
        public string Name { get; }
        
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
        /// Gets whether the entry is read-only.
        /// </summary>
        public bool IsReadOnly => Attributes.HasFlag(FileAttributes.ReadOnly);
        
        /// <summary>
        /// Gets whether the entry is hidden.
        /// </summary>
        public bool IsHidden => Attributes.HasFlag(FileAttributes.Hidden);
        
        /// <summary>
        /// Gets whether the entry is a system entry.
        /// </summary>
        public bool IsSystem => Attributes.HasFlag(FileAttributes.System);
    }
}
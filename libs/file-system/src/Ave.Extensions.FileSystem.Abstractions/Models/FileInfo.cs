namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// File-specific metadata.
    /// </summary>
    public readonly struct FileInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FileInfo"/> struct.
        /// </summary>
        /// <param name="info">The common entry information.</param>
        /// <param name="size">The size of the file in bytes.</param>
        /// <param name="extension">The file extension including the dot.</param>
        public FileInfo(EntryInfo info, long size, string? extension)
        {
            Info = info;
            Size = size;
            Extension = extension;
        }

        /// <summary>
        /// Gets the common entry information.
        /// </summary>
        public EntryInfo Info { get; }
        
        /// <summary>
        /// Gets the size of the file in bytes.
        /// </summary>
        public long Size { get; }
        
        /// <summary>
        /// Gets the file extension including the dot.
        /// </summary>
        public string? Extension { get; }
    }
}
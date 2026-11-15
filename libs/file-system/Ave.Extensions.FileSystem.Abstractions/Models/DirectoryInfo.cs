namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Directory-specific metadata.
    /// </summary>
    public readonly struct DirectoryInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DirectoryInfo"/> struct.
        /// </summary>
        /// <param name="info">The common entry information.</param>
        public DirectoryInfo(EntryInfo info)
        {
            Info = info;
        }

        /// <summary>
        /// Gets the common entry information.
        /// </summary>
        public EntryInfo Info { get; }
    }
}
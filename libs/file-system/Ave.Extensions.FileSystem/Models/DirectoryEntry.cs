namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Default implementation of IDirectoryEntry.
    /// </summary>
    public class DirectoryEntry : IDirectoryEntry
    {
        /// <summary>
        /// Gets the canonical path of the directory entry.
        /// </summary>
        public CanonicalPath Path { get; }
        
        /// <summary>
        /// Gets the directory metadata information.
        /// </summary>
        public DirectoryInfo Info { get; }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="DirectoryEntry"/> class.
        /// </summary>
        /// <param name="path">The canonical path to the directory.</param>
        /// <param name="info">The directory metadata information.</param>
        public DirectoryEntry(CanonicalPath path, DirectoryInfo info)
        {
            Path = path;
            Info = info;
        }
    }
}
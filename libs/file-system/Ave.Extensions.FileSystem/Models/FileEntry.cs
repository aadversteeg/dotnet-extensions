namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Default implementation of IFileEntry.
    /// </summary>
    public class FileEntry : IFileEntry
    {
        /// <summary>
        /// Gets the canonical path of the file entry.
        /// </summary>
        public CanonicalPath Path { get; }
        
        /// <summary>
        /// Gets the file metadata information.
        /// </summary>
        public FileInfo Info { get; }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="FileEntry"/> class.
        /// </summary>
        /// <param name="path">The canonical path to the file.</param>
        /// <param name="info">The file metadata information.</param>
        public FileEntry(CanonicalPath path, FileInfo info)
        {
            Path = path;
            Info = info;
        }
    }
}
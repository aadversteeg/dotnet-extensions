namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Represents a directory entry with its metadata.
    /// </summary>
    public interface IDirectoryEntry : IFileSystemEntry
    {
        /// <summary>
        /// Gets the directory metadata information.
        /// </summary>
        DirectoryInfo Info { get; }
    }
}
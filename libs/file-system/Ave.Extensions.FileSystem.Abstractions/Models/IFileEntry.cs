namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Represents a file entry with its metadata.
    /// </summary>
    public interface IFileEntry : IFileSystemEntry
    {
        /// <summary>
        /// Gets the file metadata information.
        /// </summary>
        FileInfo Info { get; }
    }
}
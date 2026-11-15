namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Base interface for file system entries.
    /// </summary>
    public interface IFileSystemEntry
    {
        /// <summary>
        /// Gets the canonical path of the entry.
        /// </summary>
        CanonicalPath Path { get; }
    }
}
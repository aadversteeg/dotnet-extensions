namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Extension methods for file system operations.
    /// </summary>
    public static class FileSystemExtensions
    {
        /// <summary>
        /// Returns a read-only view of the file system.
        /// </summary>
        /// <param name="fileSystem">The file system to wrap.</param>
        /// <returns>A read-only file system wrapper.</returns>
        public static IReadOnlyFileSystem AsReadOnly(this IFileSystem fileSystem)
        {
            if (fileSystem is ReadOnlyFileSystemWrapper)
            {
                return fileSystem;
            }

            return new ReadOnlyFileSystemWrapper(fileSystem);
        }
        
        /// <summary>
        /// Returns the same read-only file system when already read-only.
        /// </summary>
        /// <param name="fileSystem">The read-only file system.</param>
        /// <returns>The same read-only file system instance.</returns>
        public static IReadOnlyFileSystem AsReadOnly(this IReadOnlyFileSystem fileSystem)
        {
            return fileSystem;
        }
    }
}
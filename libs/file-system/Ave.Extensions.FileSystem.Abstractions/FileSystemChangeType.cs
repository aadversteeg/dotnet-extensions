namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Specifies the type of file system change that occurred.
    /// </summary>
    public enum FileSystemChangeType
    {
        /// <summary>
        /// A file or directory was created.
        /// </summary>
        Created,

        /// <summary>
        /// A file or directory was deleted.
        /// </summary>
        Deleted,

        /// <summary>
        /// A file or directory was changed.
        /// </summary>
        Changed,

        /// <summary>
        /// A file or directory was renamed.
        /// </summary>
        Renamed,
        
        /// <summary>
        /// A file or directory access error occurred.
        /// </summary>
        Error
    }
}
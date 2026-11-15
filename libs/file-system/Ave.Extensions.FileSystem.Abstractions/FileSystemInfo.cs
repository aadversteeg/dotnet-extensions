namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Provides immutable information about file system capabilities and characteristics.
    /// </summary>
    public readonly record struct FileSystemInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FileSystemInfo"/> record.
        /// </summary>
        /// <param name="isDurable">Indicates whether the file system persists data across application restarts.</param>
        /// <param name="supportsMonitoring">Indicates whether the file system supports real-time change monitoring.</param>
        /// <param name="isReadOnly">Indicates whether the file system is read-only.</param>
        public FileSystemInfo(bool isDurable, bool supportsMonitoring, bool isReadOnly)
        {
            IsDurable = isDurable;
            SupportsMonitoring = supportsMonitoring;
            IsReadOnly = isReadOnly;
        }

        /// <summary>
        /// Gets a value indicating whether the file system persists data across application restarts.
        /// </summary>
        /// <remarks>
        /// When true, data written to this file system will be available after the application
        /// is restarted. When false, the file system is typically in-memory only and data
        /// will be lost when the application terminates.
        /// </remarks>
        public bool IsDurable { get; }

        /// <summary>
        /// Gets a value indicating whether the file system supports real-time change monitoring.
        /// </summary>
        /// <remarks>
        /// When true, the file system can provide notifications when files or directories
        /// are created, modified, deleted, or renamed. When false, monitoring operations
        /// may not be supported or may not provide real-time notifications.
        /// </remarks>
        public bool SupportsMonitoring { get; }

        /// <summary>
        /// Gets a value indicating whether the file system is read-only.
        /// </summary>
        /// <remarks>
        /// When true, the file system does not support write operations such as creating,
        /// modifying, or deleting files and directories. When false, the file system
        /// supports both read and write operations.
        /// </remarks>
        public bool IsReadOnly { get; }
    }
}
namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Event data for file system change events.
    /// </summary>
    public readonly record struct FileSystemChangedEvent
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FileSystemChangedEvent"/> struct.
        /// </summary>
        /// <param name="path">The canonical path of the changed file.</param>
        /// <param name="changeType">The type of change that occurred.</param>
        public FileSystemChangedEvent(CanonicalPath path, FileSystemChangeType changeType)
        {
            Path = path;
            ChangeType = changeType;
        }

        /// <summary>
        /// Gets the canonical path of the changed file.
        /// </summary>
        public CanonicalPath Path { get; }

        /// <summary>
        /// Gets the type of change that occurred.
        /// </summary>
        public FileSystemChangeType ChangeType { get; }
    }
}
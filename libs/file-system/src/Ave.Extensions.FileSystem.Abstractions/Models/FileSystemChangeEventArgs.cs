using System;

namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Event arguments for file system change events.
    /// </summary>
    public class FileSystemChangeEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FileSystemChangeEventArgs"/> class.
        /// </summary>
        /// <param name="path">The canonical path of the changed file.</param>
        /// <param name="changeType">The type of change that occurred.</param>
        public FileSystemChangeEventArgs(CanonicalPath path, FileSystemChangeType changeType)
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
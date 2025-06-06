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
        /// <param name="fullPath">The full path of the changed file.</param>
        /// <param name="changeType">The type of change that occurred.</param>
        public FileSystemChangeEventArgs(string fullPath, FileSystemChangeType changeType)
        {
            FullPath = fullPath;
            ChangeType = changeType;
        }

        /// <summary>
        /// Gets the full path of the changed file.
        /// </summary>
        public string FullPath { get; }

        /// <summary>
        /// Gets the type of change that occurred.
        /// </summary>
        public FileSystemChangeType ChangeType { get; }
    }
}
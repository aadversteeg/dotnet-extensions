using System;

namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Provides data for file visited events during file system traversal.
    /// </summary>
    public class FileVisitedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the file system entry for the visited item.
        /// </summary>
        public FileSystemEntry Entry { get; }

        /// <summary>
        /// Gets the current depth of traversal.
        /// </summary>
        public int Depth { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileVisitedEventArgs"/> class.
        /// </summary>
        /// <param name="entry">The file system entry.</param>
        /// <param name="depth">The current depth.</param>
        public FileVisitedEventArgs(FileSystemEntry entry, int depth)
        {
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
            Depth = depth;
        }
    }
}
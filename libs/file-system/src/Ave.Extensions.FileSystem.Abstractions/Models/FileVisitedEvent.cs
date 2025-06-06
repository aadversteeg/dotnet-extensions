using System;

namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Event data for file visited events during file system traversal.
    /// </summary>
    public class FileVisitedEvent
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FileVisitedEvent"/> class.
        /// </summary>
        /// <param name="entry">The file system entry for the visited item.</param>
        /// <param name="depth">The current depth of traversal.</param>
        public FileVisitedEvent(FileSystemEntry entry, int depth)
        {
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
            Depth = depth;
        }

        /// <summary>
        /// Gets the file system entry for the visited item.
        /// </summary>
        public FileSystemEntry Entry { get; }

        /// <summary>
        /// Gets the current depth of traversal.
        /// </summary>
        public int Depth { get; }
    }
}
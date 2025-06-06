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
        /// Gets a value indicating whether this event represents a file visit.
        /// </summary>
        public bool IsFile => !Entry.IsDirectory;

        /// <summary>
        /// Gets a value indicating whether this event represents a directory visit.
        /// </summary>
        public bool IsDirectory => Entry.IsDirectory;

        /// <summary>
        /// Gets the current depth of traversal.
        /// </summary>
        public int Depth { get; }

        /// <summary>
        /// Gets the absolute canonical path of the visited item.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileVisitedEventArgs"/> class.
        /// </summary>
        /// <param name="entry">The file system entry.</param>
        /// <param name="depth">The current depth.</param>
        /// <param name="path">The absolute canonical path.</param>
        public FileVisitedEventArgs(FileSystemEntry entry, int depth, string path)
        {
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
            Depth = depth;
            Path = path ?? string.Empty;
        }
    }
}
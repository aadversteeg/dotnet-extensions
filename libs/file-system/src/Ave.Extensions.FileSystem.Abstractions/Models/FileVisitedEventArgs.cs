using System;

namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Provides data for file visited events during file system traversal.
    /// </summary>
    public class FileVisitedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the file metadata for the visited file.
        /// </summary>
        public FileMetadata? File { get; }

        /// <summary>
        /// Gets the directory metadata for the visited directory.
        /// </summary>
        public DirectoryMetadata? Directory { get; }

        /// <summary>
        /// Gets a value indicating whether this event represents a file visit.
        /// </summary>
        public bool IsFile => File != null;

        /// <summary>
        /// Gets a value indicating whether this event represents a directory visit.
        /// </summary>
        public bool IsDirectory => Directory != null;

        /// <summary>
        /// Gets the current depth of traversal.
        /// </summary>
        public int Depth { get; }

        /// <summary>
        /// Gets the relative path from the root of the traversal.
        /// </summary>
        public string RelativePath { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileVisitedEventArgs"/> class for a file.
        /// </summary>
        /// <param name="file">The file metadata.</param>
        /// <param name="depth">The current depth.</param>
        /// <param name="relativePath">The relative path.</param>
        public FileVisitedEventArgs(FileMetadata file, int depth, string relativePath)
        {
            File = file ?? throw new ArgumentNullException(nameof(file));
            Directory = null;
            Depth = depth;
            RelativePath = relativePath ?? string.Empty;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileVisitedEventArgs"/> class for a directory.
        /// </summary>
        /// <param name="directory">The directory metadata.</param>
        /// <param name="depth">The current depth.</param>
        /// <param name="relativePath">The relative path.</param>
        public FileVisitedEventArgs(DirectoryMetadata directory, int depth, string relativePath)
        {
            File = null;
            Directory = directory ?? throw new ArgumentNullException(nameof(directory));
            Depth = depth;
            RelativePath = relativePath ?? string.Empty;
        }
    }
}
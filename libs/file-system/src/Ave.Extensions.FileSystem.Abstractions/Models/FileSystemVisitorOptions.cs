using System;

namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Provides options for file system visitor operations.
    /// </summary>
    public class FileSystemVisitorOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether to recurse into subdirectories.
        /// </summary>
        public bool RecurseSubdirectories { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum depth to traverse. Null for unlimited depth.
        /// </summary>
        public int? MaxDepth { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to include hidden files and directories.
        /// </summary>
        public bool IncludeHidden { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to include system files and directories.
        /// </summary>
        public bool IncludeSystem { get; set; }

        /// <summary>
        /// Gets or sets a function that determines whether a directory should be visited.
        /// </summary>
        public Func<FileSystemEntry, bool>? ShouldVisitDirectory { get; set; }

        /// <summary>
        /// Gets or sets a function that determines whether a file should be visited.
        /// </summary>
        public Func<FileSystemEntry, bool>? ShouldVisitFile { get; set; }

        /// <summary>
        /// Gets or sets a function called when visiting a directory.
        /// </summary>
        public Func<FileSystemEntry, FileSystemVisitorAction>? OnDirectoryVisited { get; set; }

        /// <summary>
        /// Gets or sets a function called when visiting a file.
        /// </summary>
        public Func<FileSystemEntry, FileSystemVisitorAction>? OnFileVisited { get; set; }

        /// <summary>
        /// Gets or sets a function called when an error occurs during traversal.
        /// </summary>
        public Func<FileSystemError, FileSystemVisitorAction>? OnError { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to follow symbolic links.
        /// </summary>
        public bool FollowSymbolicLinks { get; set; }

        /// <summary>
        /// Gets the default visitor options.
        /// </summary>
        public static FileSystemVisitorOptions Default => new FileSystemVisitorOptions
        {
            RecurseSubdirectories = true,
            MaxDepth = null,
            IncludeHidden = false,
            IncludeSystem = false,
            FollowSymbolicLinks = false
        };
    }

    /// <summary>
    /// Defines actions that can be taken during file system traversal.
    /// </summary>
    public enum FileSystemVisitorAction
    {
        /// <summary>
        /// Continue with the traversal.
        /// </summary>
        Continue,

        /// <summary>
        /// Skip the current directory and its subdirectories.
        /// </summary>
        SkipSubtree,

        /// <summary>
        /// Stop the entire traversal.
        /// </summary>
        Stop
    }
}
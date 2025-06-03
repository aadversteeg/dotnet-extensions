using System.Collections.Generic;

namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Contains the results of a file system visitor operation.
    /// </summary>
    public class FileSystemVisitResult
    {
        /// <summary>
        /// Gets or sets the total number of files visited.
        /// </summary>
        public int FilesVisited { get; set; }

        /// <summary>
        /// Gets or sets the total number of directories visited.
        /// </summary>
        public int DirectoriesVisited { get; set; }

        /// <summary>
        /// Gets or sets the total size of all files visited, in bytes.
        /// </summary>
        public long TotalSize { get; set; }

        /// <summary>
        /// Gets or sets the errors that occurred during traversal.
        /// </summary>
        public List<FileSystemError> Errors { get; set; } = new List<FileSystemError>();

        /// <summary>
        /// Gets a value indicating whether the traversal completed without being stopped.
        /// </summary>
        public bool IsCompleted { get; set; } = true;

        /// <summary>
        /// Gets a value indicating whether any errors occurred during traversal.
        /// </summary>
        public bool HasErrors => Errors.Count > 0;

        /// <summary>
        /// Gets the total number of entries (files and directories) visited.
        /// </summary>
        public int TotalEntriesVisited => FilesVisited + DirectoriesVisited;
    }
}
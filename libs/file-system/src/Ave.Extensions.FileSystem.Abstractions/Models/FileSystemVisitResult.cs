using System.Collections.Generic;

namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Contains the results of a file system visitor operation.
    /// </summary>
    public class FileSystemVisitResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FileSystemVisitResult"/> class.
        /// </summary>
        /// <param name="filesVisited">The total number of files visited.</param>
        /// <param name="directoriesVisited">The total number of directories visited.</param>
        /// <param name="totalSize">The total size of all files visited, in bytes.</param>
        /// <param name="errors">The errors that occurred during traversal.</param>
        /// <param name="isCompleted">Whether the traversal completed without being stopped.</param>
        public FileSystemVisitResult(
            int filesVisited,
            int directoriesVisited,
            long totalSize,
            List<FileSystemError> errors,
            bool isCompleted = true)
        {
            FilesVisited = filesVisited;
            DirectoriesVisited = directoriesVisited;
            TotalSize = totalSize;
            Errors = errors ?? new List<FileSystemError>();
            IsCompleted = isCompleted;
        }

        /// <summary>
        /// Gets the total number of files visited.
        /// </summary>
        public int FilesVisited { get; }

        /// <summary>
        /// Gets the total number of directories visited.
        /// </summary>
        public int DirectoriesVisited { get; }

        /// <summary>
        /// Gets the total size of all files visited, in bytes.
        /// </summary>
        public long TotalSize { get; }

        /// <summary>
        /// Gets the errors that occurred during traversal.
        /// </summary>
        public List<FileSystemError> Errors { get; }

        /// <summary>
        /// Gets a value indicating whether the traversal completed without being stopped.
        /// </summary>
        public bool IsCompleted { get; }

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
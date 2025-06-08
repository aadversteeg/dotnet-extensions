using System;
using System.Collections.Generic;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using FileSystemSamples.Common.Progress;

namespace FileCounter.Handlers
{
    /// <summary>
    /// Base interface for all counting handlers that track file system statistics.
    /// </summary>
    public interface ICountingHandler : ITraversalHandler<FileSystemEntry>, IProgressAware
    {
        /// <summary>
        /// Gets the total number of files processed.
        /// </summary>
        int FileCount { get; }

        /// <summary>
        /// Gets the total number of directories processed.
        /// </summary>
        int DirectoryCount { get; }

        /// <summary>
        /// Gets the total size in bytes of all processed files.
        /// </summary>
        long TotalSize { get; }

        /// <summary>
        /// Gets the number of errors encountered during processing.
        /// </summary>
        int ErrorCount { get; }

        /// <summary>
        /// Gets the list of errors encountered during processing.
        /// </summary>
        IReadOnlyList<string> Errors { get; }

        /// <summary>
        /// Gets the number of directories that were skipped due to errors.
        /// </summary>
        int SkippedDirectories { get; }

        /// <summary>
        /// Gets the elapsed processing time.
        /// </summary>
        TimeSpan ElapsedTime { get; }

        /// <summary>
        /// Gets a human-readable summary of the counting results.
        /// </summary>
        /// <returns>A string containing the formatted results.</returns>
        string GetSummary();
    }
}
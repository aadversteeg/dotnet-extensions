using System.Threading;

namespace Ave.Extensions.FileSystem.Traversal
{
    /// <summary>
    /// Thread-safe statistics for traversal operations.
    /// </summary>
    public sealed class TraversalStats
    {
        private long filesProcessed;
        private long directoriesProcessed;
        private long totalBytesProcessed;

        /// <summary>
        /// Gets the number of files processed.
        /// </summary>
        public long FilesProcessed => Interlocked.Read(ref filesProcessed);
        
        /// <summary>
        /// Gets the number of directories processed.
        /// </summary>
        public long DirectoriesProcessed => Interlocked.Read(ref directoriesProcessed);
        
        /// <summary>
        /// Gets the total bytes processed.
        /// </summary>
        public long TotalBytesProcessed => Interlocked.Read(ref totalBytesProcessed);

        /// <summary>
        /// Increments the file count.
        /// </summary>
        public void IncrementFiles() => Interlocked.Increment(ref filesProcessed);
        
        /// <summary>
        /// Increments the directory count.
        /// </summary>
        public void IncrementDirectories() => Interlocked.Increment(ref directoriesProcessed);
        
        /// <summary>
        /// Adds bytes to the total.
        /// </summary>
        /// <param name="bytes">The number of bytes to add.</param>
        public void AddBytes(long bytes) => Interlocked.Add(ref totalBytesProcessed, bytes);
    }
}
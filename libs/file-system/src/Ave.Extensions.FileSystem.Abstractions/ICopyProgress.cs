namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Provides progress information for copy and move operations.
    /// </summary>
    public interface ICopyProgress
    {
        /// <summary>
        /// Gets the total number of bytes transferred so far.
        /// </summary>
        long BytesTransferred { get; }
        
        /// <summary>
        /// Gets the total number of bytes to transfer.
        /// </summary>
        long TotalBytes { get; }
        
        /// <summary>
        /// Gets the path of the file currently being processed.
        /// </summary>
        string CurrentFile { get; }
        
        /// <summary>
        /// Gets the number of files processed so far.
        /// </summary>
        int FilesProcessed { get; }
        
        /// <summary>
        /// Gets the total number of files to process.
        /// </summary>
        int TotalFiles { get; }
    }
}
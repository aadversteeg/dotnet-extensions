using System;

namespace Ave.Extensions.FileSystem.Progress
{
    /// <summary>
    /// Provides progress information for copy and move operations.
    /// </summary>
    public class CopyProgress : ICopyProgress
    {
        /// <summary>
        /// Initializes a new instance of CopyProgress.
        /// </summary>
        public CopyProgress(
            long bytesTransferred,
            long totalBytes,
            string currentFile,
            int filesProcessed,
            int totalFiles)
        {
            BytesTransferred = bytesTransferred;
            TotalBytes = totalBytes;
            CurrentFile = currentFile;
            FilesProcessed = filesProcessed;
            TotalFiles = totalFiles;
        }

        /// <inheritdoc />
        public long BytesTransferred { get; }
        
        /// <inheritdoc />
        public long TotalBytes { get; }
        
        /// <inheritdoc />
        public string CurrentFile { get; }
        
        /// <inheritdoc />
        public int FilesProcessed { get; }
        
        /// <inheritdoc />
        public int TotalFiles { get; }
        
        /// <summary>
        /// Gets the percentage complete (0-100).
        /// </summary>
        public double PercentComplete => 
            TotalBytes > 0 ? (double)BytesTransferred / TotalBytes * 100 : 0;
        
        /// <summary>
        /// Creates a copy of this progress with updated values.
        /// </summary>
        public CopyProgress WithUpdate(
            long? bytesTransferred = null,
            string? currentFile = null,
            int? filesProcessed = null)
        {
            return new CopyProgress(
                bytesTransferred ?? BytesTransferred,
                TotalBytes,
                currentFile ?? CurrentFile,
                filesProcessed ?? FilesProcessed,
                TotalFiles);
        }
    }
}
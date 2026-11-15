using System;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Options for copy operations.
    /// </summary>
    public class CopyOptions
    {
        /// <summary>
        /// Gets or sets whether to overwrite existing files.
        /// Default is false.
        /// </summary>
        public bool Overwrite { get; set; }
        
        /// <summary>
        /// Gets or sets whether to preserve file timestamps during copy.
        /// Default is true.
        /// </summary>
        public bool PreserveTimestamps { get; set; } = true;
        
        /// <summary>
        /// Gets or sets whether to preserve file attributes during copy.
        /// Default is true.
        /// </summary>
        public bool PreserveAttributes { get; set; } = true;
        
        /// <summary>
        /// Gets or sets the progress reporter for the operation.
        /// </summary>
        public IProgress<ICopyProgress>? Progress { get; set; }
        
        /// <summary>
        /// Gets or sets the buffer size for copy operations.
        /// Default is 81920 (80KB).
        /// </summary>
        public int BufferSize { get; set; } = 81920;
        
        /// <summary>
        /// Gets a default instance of CopyOptions.
        /// </summary>
        public static CopyOptions Default => new CopyOptions();
    }
}
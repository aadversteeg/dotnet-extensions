using System;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Options for move operations.
    /// </summary>
    public class MoveOptions
    {
        /// <summary>
        /// Gets or sets whether to overwrite existing files.
        /// Default is false.
        /// </summary>
        public bool Overwrite { get; set; }
        
        /// <summary>
        /// Gets or sets whether to preserve file timestamps during move.
        /// Default is true.
        /// </summary>
        public bool PreserveTimestamps { get; set; } = true;
        
        /// <summary>
        /// Gets or sets whether to preserve file attributes during move.
        /// Default is true.
        /// </summary>
        public bool PreserveAttributes { get; set; } = true;
        
        /// <summary>
        /// Gets or sets the progress reporter for the operation.
        /// </summary>
        public IProgress<ICopyProgress>? Progress { get; set; }
        
        /// <summary>
        /// Gets a default instance of MoveOptions.
        /// </summary>
        public static MoveOptions Default => new MoveOptions();
    }
}
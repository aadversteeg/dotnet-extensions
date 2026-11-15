using System.Collections.Generic;

namespace Ave.Extensions.FileSystem.Configuration
{
    /// <summary>
    /// Configuration options for file system services.
    /// </summary>
    public class FileSystemOptions
    {
        /// <summary>
        /// List of allowed directory paths for security validation
        /// </summary>
        public List<string> AllowedPaths { get; set; } = new();
        
        /// <summary>
        /// Enable path security validation
        /// </summary>
        public bool EnablePathSecurity { get; set; } = false;
        
        /// <summary>
        /// Use canonical path mapping (Unix-style paths)
        /// </summary>
        public bool UseCanonicalPaths { get; set; } = false;
        
        

        /// <summary>
        /// Gets or sets a value indicating whether to use async enumeration features.
        /// </summary>
        public bool UseAsyncEnumeration { get; set; } = true;

        /// <summary>
        /// Gets or sets the default buffer size for I/O operations.
        /// </summary>
        public int DefaultBufferSize { get; set; } = 4096;



        /// <summary>
        /// Gets or sets a value indicating whether to register the read-only wrapper.
        /// </summary>
        public bool RegisterReadOnlyWrapper { get; set; } = true;
    }
}
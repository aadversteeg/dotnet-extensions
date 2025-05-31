namespace Ave.Extensions.FileSystem.DependencyInjection
{
    /// <summary>
    /// Configuration options for file system services.
    /// </summary>
    public class FileSystemOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether to enable monitoring capabilities.
        /// </summary>
        public bool EnableMonitoring { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to use async enumeration features.
        /// </summary>
        public bool UseAsyncEnumeration { get; set; } = true;

        /// <summary>
        /// Gets or sets the default buffer size for I/O operations.
        /// </summary>
        public int DefaultBufferSize { get; set; } = 4096;

        /// <summary>
        /// Gets or sets a value indicating whether to register path utilities.
        /// </summary>
        public bool RegisterPathUtilities { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to register the file system visitor.
        /// </summary>
        public bool RegisterVisitor { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to register the read-only wrapper.
        /// </summary>
        public bool RegisterReadOnlyWrapper { get; set; } = true;
    }
}
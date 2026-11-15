namespace Ave.Extensions.FileSystem.MimeDetective.Configuration
{
    /// <summary>
    /// Configuration options for content type detection.
    /// </summary>
    public class ContentTypeMapperOptions
    {
        /// <summary>
        /// Maximum number of bytes to read from file for detection.
        /// Default: 8192 bytes
        /// </summary>
        public int MaxBytesToRead { get; set; } = 8192;

        /// <summary>
        /// Whether to include alternative file extensions in results.
        /// Default: true
        /// </summary>
        public bool IncludeAlternativeExtensions { get; set; } = true;

        /// <summary>
        /// Whether to include file categories in results.
        /// Default: true
        /// </summary>
        public bool IncludeCategories { get; set; } = true;

        /// <summary>
        /// Whether to fallback to extension-based detection if content detection fails.
        /// Default: true
        /// </summary>
        public bool FallbackToExtension { get; set; } = true;

        /// <summary>
        /// Which definition pack to use (Default, Condensed, Exhaustive).
        /// Default: DefinitionPack.Default
        /// </summary>
        public DefinitionPack DefinitionPack { get; set; } = DefinitionPack.Default;

        /// <summary>
        /// Default content type to use when detection fails.
        /// Default: "application/octet-stream"
        /// </summary>
        public string DefaultContentType { get; set; } = "application/octet-stream";
    }

    /// <summary>
    /// Available definition packs for content type detection.
    /// </summary>
    public enum DefinitionPack
    {
        /// <summary>
        /// Default definition pack with balanced accuracy and performance.
        /// </summary>
        Default,

        /// <summary>
        /// Condensed definition pack for better performance with fewer signatures.
        /// </summary>
        Condensed,

        /// <summary>
        /// Exhaustive definition pack with maximum accuracy and signatures.
        /// </summary>
        Exhaustive
    }
}
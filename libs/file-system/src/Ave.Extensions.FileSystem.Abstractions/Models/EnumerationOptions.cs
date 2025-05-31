using System;

namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Provides options for file system enumeration operations.
    /// </summary>
    public class EnumerationOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether to recurse into subdirectories.
        /// </summary>
        public bool RecurseSubdirectories { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to include hidden files and directories.
        /// </summary>
        public bool IncludeHidden { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to include system files and directories.
        /// </summary>
        public bool IncludeSystem { get; set; }

        /// <summary>
        /// Gets or sets the maximum depth to recurse. Null for unlimited depth.
        /// </summary>
        public int? MaxDepth { get; set; }

        /// <summary>
        /// Gets or sets a function that determines whether a directory should be included in the enumeration.
        /// </summary>
        public Func<string, bool>? ShouldIncludeDirectory { get; set; }

        /// <summary>
        /// Gets or sets a function that determines whether a file should be included in the enumeration.
        /// </summary>
        public Func<string, bool>? ShouldIncludeFile { get; set; }

        /// <summary>
        /// Gets the default enumeration options.
        /// </summary>
        public static EnumerationOptions Default => new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IncludeHidden = false,
            IncludeSystem = false,
            MaxDepth = null,
            ShouldIncludeDirectory = null,
            ShouldIncludeFile = null
        };

        /// <summary>
        /// Creates enumeration options for recursive traversal.
        /// </summary>
        /// <param name="maxDepth">The maximum depth to recurse.</param>
        /// <returns>New enumeration options configured for recursion.</returns>
        public static EnumerationOptions Recursive(int? maxDepth = null) => new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IncludeHidden = false,
            IncludeSystem = false,
            MaxDepth = maxDepth,
            ShouldIncludeDirectory = null,
            ShouldIncludeFile = null
        };

        /// <summary>
        /// Creates enumeration options that include all entries (including hidden and system).
        /// </summary>
        /// <returns>New enumeration options configured to include all entries.</returns>
        public static EnumerationOptions IncludeAll() => new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IncludeHidden = true,
            IncludeSystem = true,
            MaxDepth = null,
            ShouldIncludeDirectory = null,
            ShouldIncludeFile = null
        };
    }
}
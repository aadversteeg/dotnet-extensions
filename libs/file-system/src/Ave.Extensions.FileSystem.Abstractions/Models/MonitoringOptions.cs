using System;
using System.Collections.Generic;

namespace Ave.Extensions.FileSystem.Models
{
    /// <summary>
    /// Provides options for file system monitoring operations.
    /// </summary>
    public class MonitoringOptions
    {
        /// <summary>
        /// Gets or sets the path to monitor.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether to monitor subdirectories.
        /// </summary>
        public bool IncludeSubdirectories { get; set; }

        /// <summary>
        /// Gets or sets the file system changes to monitor.
        /// </summary>
        public FileSystemNotifyFilters NotifyFilters { get; set; } = FileSystemNotifyFilters.All;

        /// <summary>
        /// Gets or sets patterns of files to include in monitoring.
        /// </summary>
        public List<string> IncludePatterns { get; set; } = new List<string> { "*" };

        /// <summary>
        /// Gets or sets patterns of files to exclude from monitoring.
        /// </summary>
        public List<string> ExcludePatterns { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets a value indicating whether to include hidden files.
        /// </summary>
        public bool IncludeHidden { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to include system files.
        /// </summary>
        public bool IncludeSystem { get; set; }

        /// <summary>
        /// Gets or sets the buffer size for file system events.
        /// </summary>
        public int BufferSize { get; set; } = 8192;

        /// <summary>
        /// Gets or sets a custom filter function for file changes.
        /// </summary>
        public Func<FileSystemChangeEventArgs, bool>? CustomFilter { get; set; }

        /// <summary>
        /// Gets default monitoring options for a path.
        /// </summary>
        /// <param name="path">The path to monitor.</param>
        /// <returns>Default monitoring options.</returns>
        public static MonitoringOptions ForPath(string path) => new MonitoringOptions
        {
            Path = path,
            IncludeSubdirectories = false,
            NotifyFilters = FileSystemNotifyFilters.All,
            IncludePatterns = new List<string> { "*" },
            ExcludePatterns = new List<string>(),
            IncludeHidden = false,
            IncludeSystem = false
        };

        /// <summary>
        /// Creates monitoring options for recursive monitoring.
        /// </summary>
        /// <param name="path">The path to monitor.</param>
        /// <returns>Monitoring options configured for recursive monitoring.</returns>
        public static MonitoringOptions Recursive(string path) => new MonitoringOptions
        {
            Path = path,
            IncludeSubdirectories = true,
            NotifyFilters = FileSystemNotifyFilters.All,
            IncludePatterns = new List<string> { "*" },
            ExcludePatterns = new List<string>(),
            IncludeHidden = false,
            IncludeSystem = false
        };
    }
}
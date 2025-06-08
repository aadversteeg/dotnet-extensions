using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;

namespace FileCounter.Handlers
{
    /// <summary>
    /// Detailed counting handler that provides per-extension breakdown and size distribution.
    /// </summary>
    public class DetailedCounterHandler : SimpleCounterHandler
    {
        private readonly ConcurrentDictionary<string, ExtensionStats> _extensionStats;
        private readonly int _maxExtensions;

        /// <summary>
        /// Initializes a new instance of the DetailedCounterHandler class.
        /// </summary>
        /// <param name="extensionFilter">Optional file extension filter (e.g., ".cs").</param>
        /// <param name="minSize">Minimum file size in bytes.</param>
        /// <param name="maxSize">Maximum file size in bytes.</param>
        /// <param name="maxExtensions">Maximum number of extensions to show in text format (0 = show all).</param>
        /// <param name="logger">Logger for debug information.</param>
        public DetailedCounterHandler(string? extensionFilter = null, long minSize = 0, long maxSize = long.MaxValue, int maxExtensions = 20, Microsoft.Extensions.Logging.ILogger? logger = null)
            : base(extensionFilter, minSize, maxSize, logger)
        {
            _extensionStats = new ConcurrentDictionary<string, ExtensionStats>();
            _maxExtensions = maxExtensions;
        }

        /// <summary>
        /// Gets the extension statistics collected during traversal.
        /// </summary>
        public IReadOnlyDictionary<string, ExtensionStats> ExtensionStats => _extensionStats;

        /// <inheritdoc />
        public override string GetSummary()
        {
            var summary = new StringBuilder();

            // Add extension breakdown if we have multiple extensions
            if (_extensionStats.Count > 1)
            {
                summary.AppendLine("File Count by Extension:");
                
                var sortedExtensions = _extensionStats.OrderByDescending(kvp => kvp.Value.Count);
                
                if (_maxExtensions == 0)
                {
                    // Show all extensions
                    foreach (var kvp in sortedExtensions)
                    {
                        var ext = string.IsNullOrEmpty(kvp.Key) ? "[no extension]" : kvp.Key;
                        summary.AppendLine($"{ext,-12} {kvp.Value.Count,8:N0} files    {FormatSize(kvp.Value.TotalSize),10}");
                    }
                }
                else
                {
                    // Show limited number of extensions
                    var topExtensions = sortedExtensions.Take(_maxExtensions);
                    
                    foreach (var kvp in topExtensions)
                    {
                        var ext = string.IsNullOrEmpty(kvp.Key) ? "[no extension]" : kvp.Key;
                        summary.AppendLine($"{ext,-12} {kvp.Value.Count,8:N0} files    {FormatSize(kvp.Value.TotalSize),10}");
                    }

                    if (_extensionStats.Count > _maxExtensions)
                    {
                        var remaining = _extensionStats.Skip(_maxExtensions).Sum(kvp => kvp.Value.Count);
                        var remainingSize = _extensionStats.Skip(_maxExtensions).Sum(kvp => kvp.Value.TotalSize);
                        var remainingCount = _extensionStats.Count - _maxExtensions;
                        summary.AppendLine($"[{remainingCount} others] {remaining,8:N0} files    {FormatSize(remainingSize),10}");
                    }
                }

                summary.AppendLine();
            }

            // Add totals
            summary.AppendLine($"Start Path: {StartPath.Value}");
            summary.AppendLine($"Total: {FileCount:N0} files ({FormatSize(TotalSize)}) in {DirectoryCount:N0} directories");
            summary.AppendLine($"Time: {FormatTime(ElapsedTime)}");

            if (ErrorCount > 0)
            {
                summary.AppendLine($"Errors: {ErrorCount:N0} (access denied or other issues)");
                summary.AppendLine($"Skipped: {SkippedDirectories:N0} directories");
            }

            if (ElapsedTime.TotalSeconds > 0)
            {
                var filesPerSecond = FileCount / ElapsedTime.TotalSeconds;
                summary.AppendLine($"Speed: {filesPerSecond:N0} files/second");
            }

            return summary.ToString().TrimEnd();
        }

        /// <inheritdoc />
        public override Task<Result<TraversalControl, FileSystemError>> OnFileAsync(FileSystemEntry file, TraversalContext context, CancellationToken cancellationToken = default)
        {
            if (!file.IsDirectory)
            {
                var size = file.Size ?? 0;
                var extension = file.Extension ?? string.Empty;

                // Track extension statistics regardless of filters
                _extensionStats.AddOrUpdate(
                    extension,
                    new ExtensionStats { Count = 1, TotalSize = size },
                    (key, existing) => new ExtensionStats 
                    { 
                        Count = existing.Count + 1, 
                        TotalSize = existing.TotalSize + size 
                    });
            }

            // Call base implementation for filtering and counting
            return base.OnFileAsync(file, context, cancellationToken);
        }
    }

    /// <summary>
    /// Statistics for a particular file extension.
    /// </summary>
    public record ExtensionStats
    {
        /// <summary>
        /// Gets or sets the number of files with this extension.
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// Gets or sets the total size of all files with this extension.
        /// </summary>
        public long TotalSize { get; set; }
    }
}
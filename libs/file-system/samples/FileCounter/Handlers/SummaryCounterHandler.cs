using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;

namespace FileCounter.Handlers
{
    /// <summary>
    /// Summary counting handler that provides performance metrics and file statistics.
    /// </summary>
    public class SummaryCounterHandler : SimpleCounterHandler
    {
        private int _maxDepth;
        private string _largestFileName = string.Empty;
        private long _largestFileSize;
        private DateTime _oldestFileTime = DateTime.MaxValue;
        private DateTime _newestFileTime = DateTime.MinValue;

        /// <summary>
        /// Initializes a new instance of the SummaryCounterHandler class.
        /// </summary>
        /// <param name="extensionFilter">Optional file extension filter (e.g., ".cs").</param>
        /// <param name="minSize">Minimum file size in bytes.</param>
        /// <param name="maxSize">Maximum file size in bytes.</param>
        /// <param name="logger">Logger for debug information.</param>
        public SummaryCounterHandler(string? extensionFilter = null, long minSize = 0, long maxSize = long.MaxValue, Microsoft.Extensions.Logging.ILogger? logger = null)
            : base(extensionFilter, minSize, maxSize, logger)
        {
        }

        /// <summary>
        /// Gets the maximum depth reached during traversal.
        /// </summary>
        public int MaxDepth => _maxDepth;

        /// <summary>
        /// Gets the name of the largest file encountered.
        /// </summary>
        public string LargestFileName => _largestFileName;

        /// <summary>
        /// Gets the size of the largest file encountered.
        /// </summary>
        public long LargestFileSize => _largestFileSize;

        /// <summary>
        /// Gets the modification time of the oldest file encountered.
        /// </summary>
        public DateTime OldestFileTime => _oldestFileTime;

        /// <summary>
        /// Gets the modification time of the newest file encountered.
        /// </summary>
        public DateTime NewestFileTime => _newestFileTime;


        /// <inheritdoc />
        public override string GetSummary()
        {
            var summary = new StringBuilder();
            
            // Basic counts
            summary.AppendLine($"Files:       {FileCount:N0}");
            summary.AppendLine($"Directories: {DirectoryCount:N0}");
            summary.AppendLine($"Total Size:  {FormatSize(TotalSize)}");
            summary.AppendLine($"Time:        {FormatTime(ElapsedTime)}");

            // Extended statistics
            if (MaxDepth > 0)
            {
                summary.AppendLine($"Max Depth:   {MaxDepth}");
            }

            if (_largestFileSize > 0)
            {
                summary.AppendLine($"Largest:     {_largestFileName} ({FormatSize(_largestFileSize)})");
            }

            if (_oldestFileTime != DateTime.MaxValue && _newestFileTime != DateTime.MinValue)
            {
                summary.AppendLine($"Oldest:      {_oldestFileTime:yyyy-MM-dd HH:mm:ss}");
                summary.AppendLine($"Newest:      {_newestFileTime:yyyy-MM-dd HH:mm:ss}");
            }

            if (ErrorCount > 0)
            {
                summary.AppendLine($"Errors:      {ErrorCount:N0} (access denied or other issues)");
                summary.AppendLine($"Skipped:     {SkippedDirectories:N0} directories");
            }

            // Performance metrics
            if (ElapsedTime.TotalSeconds > 0)
            {
                var filesPerSecond = FileCount / ElapsedTime.TotalSeconds;
                summary.AppendLine($"Speed:       {filesPerSecond:N0} files/second");
            }

            return summary.ToString().TrimEnd();
        }

        /// <inheritdoc />
        public override Task<Result<Unit, Error>> OnTraversalStartAsync(CanonicalPath rootPath, CancellationToken cancellationToken = default)
        {
            return base.OnTraversalStartAsync(rootPath, cancellationToken);
        }

        /// <inheritdoc />
        public override Task<Result<TraversalControl, Error>> OnDirectoryEnterAsync(FileSystemEntry directory, TraversalContext context, CancellationToken cancellationToken = default)
        {
            // Track maximum depth
            if (context.CurrentDepth > _maxDepth)
            {
                _maxDepth = context.CurrentDepth;
            }

            return base.OnDirectoryEnterAsync(directory, context, cancellationToken);
        }

        /// <inheritdoc />
        public override Task<Result<TraversalControl, Error>> OnFileAsync(FileSystemEntry file, TraversalContext context, CancellationToken cancellationToken = default)
        {
            if (!file.IsDirectory)
            {
                var size = file.Size ?? 0;
                var lastModified = file.ModifiedUtc;

                // Track largest file
                if (size > _largestFileSize)
                {
                    _largestFileSize = size;
                    _largestFileName = file.Name;
                }

                // Track date range
                if (lastModified < _oldestFileTime)
                {
                    _oldestFileTime = lastModified;
                }

                if (lastModified > _newestFileTime)
                {
                    _newestFileTime = lastModified;
                }

                // Track maximum depth
                if (context.CurrentDepth > _maxDepth)
                {
                    _maxDepth = context.CurrentDepth;
                }
            }

            return base.OnFileAsync(file, context, cancellationToken);
        }

        /// <inheritdoc />
        public override Task<Result<Unit, Error>> OnTraversalCompleteAsync(TraversalSummary summary, CancellationToken cancellationToken = default)
        {
            return base.OnTraversalCompleteAsync(summary, cancellationToken);
        }
    }
}
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;
using Microsoft.Extensions.Logging;

namespace FileCounter.Handlers
{
    /// <summary>
    /// Simple counting handler that tracks basic file and directory statistics.
    /// </summary>
    public class SimpleCounterHandler : ICountingHandler
    {
        private readonly string? _extensionFilter;
        private readonly long _minSize;
        private readonly long _maxSize;
        private int _fileCount;
        private int _directoryCount;
        private long _totalSize;
        private int _errorCount;
        private int _skippedDirectories;
        private readonly ConcurrentBag<string> _errors;
        private readonly Stopwatch _stopwatch;
        private CanonicalPath _startPath;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the SimpleCounterHandler class.
        /// </summary>
        /// <param name="extensionFilter">Optional file extension filter (e.g., ".cs").</param>
        /// <param name="minSize">Minimum file size in bytes.</param>
        /// <param name="maxSize">Maximum file size in bytes.</param>
        /// <param name="logger">Logger for debug information.</param>
        public SimpleCounterHandler(string? extensionFilter = null, long minSize = 0, long maxSize = long.MaxValue, ILogger? logger = null)
        {
            _extensionFilter = extensionFilter;
            _minSize = minSize;
            _maxSize = maxSize;
            _errors = new ConcurrentBag<string>();
            _stopwatch = new Stopwatch();
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        }

        /// <inheritdoc />
        public int FileCount => _fileCount;

        /// <inheritdoc />
        public int DirectoryCount => _directoryCount;

        /// <inheritdoc />
        public long TotalSize => _totalSize;

        /// <inheritdoc />
        public int ErrorCount => _errorCount;

        /// <inheritdoc />
        public IReadOnlyList<string> Errors => _errors.ToList();

        /// <summary>
        /// Gets the number of directories that were skipped due to errors.
        /// </summary>
        public int SkippedDirectories => _skippedDirectories;

        /// <summary>
        /// Gets the elapsed processing time.
        /// </summary>
        public TimeSpan ElapsedTime => _stopwatch.Elapsed;

        /// <summary>
        /// Gets the canonical path where traversal started.
        /// </summary>
        public CanonicalPath StartPath => _startPath;

        /// <inheritdoc />
        public virtual string GetSummary()
        {
            var summary = $"Start Path:  {StartPath.Value}\n" +
                         $"Files:       {FileCount:N0}\n" +
                         $"Directories: {DirectoryCount:N0}\n" +
                         $"Total Size:  {FormatSize(TotalSize)}\n" +
                         $"Time:        {FormatTime(ElapsedTime)}";

            if (ErrorCount > 0)
            {
                summary += $"\nErrors:      {ErrorCount:N0} (access denied or other issues)";
                summary += $"\nSkipped:     {SkippedDirectories:N0} directories";
            }

            if (ElapsedTime.TotalSeconds > 0)
            {
                var filesPerSecond = FileCount / ElapsedTime.TotalSeconds;
                summary += $"\nSpeed:       {filesPerSecond:N0} files/second";
            }

            return summary;
        }

        /// <inheritdoc />
        public virtual Task<Result<Unit, FileSystemError>> OnTraversalStartAsync(CanonicalPath rootPath, CancellationToken cancellationToken = default)
        {
            _startPath = rootPath;
            _stopwatch.Start();
            return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
        }

        /// <inheritdoc />
        public virtual Task<Result<TraversalControl, FileSystemError>> OnDirectoryEnterAsync(FileSystemEntry directory, TraversalContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Entering directory: {DirectoryPath} (depth: {CurrentDepth})", directory.Path, context.CurrentDepth);
                return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
            }
            catch (Exception ex)
            {
                LogError($"Error entering directory {directory.Path}: {ex.Message}");
                return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
            }
        }

        /// <inheritdoc />
        public virtual Task<Result<TraversalControl, FileSystemError>> OnFileAsync(FileSystemEntry file, TraversalContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!file.IsDirectory)
                {
                    _logger.LogDebug("Processing file: {FilePath} (size: {FileSize})", file.Path, file.Size ?? 0);
                    
                    var size = file.Size ?? 0;

                    // Apply size filters
                    if (size < _minSize || size > _maxSize)
                    {
                        return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
                    }

                    // Apply extension filter
                    if (_extensionFilter != null && !string.Equals(file.Extension, _extensionFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
                    }

                    Interlocked.Increment(ref _fileCount);
                    Interlocked.Add(ref _totalSize, size);
                }

                return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
            }
            catch (Exception ex)
            {
                LogError($"Error processing file {file.Path}: {ex.Message}");
                return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
            }
        }

        /// <inheritdoc />
        public virtual Task<Result<TraversalControl, FileSystemError>> OnDirectoryAsync(FileSystemEntry directory, TraversalContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                if (context.CurrentDepth > 0) // Don't count the root directory
                {
                    Interlocked.Increment(ref _directoryCount);
                }
                return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
            }
            catch (Exception ex)
            {
                LogError($"Error processing directory {directory.Path}: {ex.Message}");
                return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
            }
        }

        /// <inheritdoc />
        public virtual Task<Result<Unit, FileSystemError>> OnTraversalCompleteAsync(TraversalSummary summary, CancellationToken cancellationToken = default)
        {
            _stopwatch.Stop();
            return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
        }

        /// <summary>
        /// Logs an error and increments the error counter.
        /// </summary>
        /// <param name="error">The error message to log.</param>
        protected virtual void LogError(string error)
        {
            _errors.Add(error);
            Interlocked.Increment(ref _errorCount);
            
            // If it's a directory access error, count it as skipped
            if (error.Contains("directory") || error.Contains("Access to the path"))
            {
                Interlocked.Increment(ref _skippedDirectories);
            }
        }

        /// <summary>
        /// Formats a byte count into a human-readable size string.
        /// </summary>
        /// <param name="bytes">The number of bytes.</param>
        /// <returns>A formatted string representation of the size.</returns>
        protected static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        /// <summary>
        /// Formats a TimeSpan into a human-readable time string.
        /// </summary>
        /// <param name="timeSpan">The TimeSpan to format.</param>
        /// <returns>A formatted string representation of the time.</returns>
        protected static string FormatTime(TimeSpan timeSpan)
        {
            if (timeSpan.TotalDays >= 1)
                return $"{timeSpan.Days}d {timeSpan.Hours}h {timeSpan.Minutes}m {timeSpan.Seconds}s";
            if (timeSpan.TotalHours >= 1)
                return $"{timeSpan.Hours}h {timeSpan.Minutes}m {timeSpan.Seconds}s";
            if (timeSpan.TotalMinutes >= 1)
                return $"{timeSpan.Minutes}m {timeSpan.Seconds}s";
            if (timeSpan.TotalSeconds >= 1)
                return $"{timeSpan.TotalSeconds:F1}s";
            return $"{timeSpan.TotalMilliseconds:F0}ms";
        }
    }
}
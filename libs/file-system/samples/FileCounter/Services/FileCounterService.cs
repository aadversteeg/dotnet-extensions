using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;
using FileCounter.Export;
using FileCounter.Handlers;
using FileSystemSamples.Common.Progress;
using Microsoft.Extensions.Logging;

namespace FileCounter.Services
{
    /// <summary>
    /// Service for executing file counting operations with various configurations.
    /// </summary>
    public class FileCounterService
    {
        private readonly IFileSystem _fileSystem;
        private readonly HandlerFactory _handlerFactory;
        private readonly ILogger<FileCounterService> _logger;
        private readonly IPathMapper _pathMapper;
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// Initializes a new instance of the FileCounterService class.
        /// </summary>
        /// <param name="fileSystem">The file system to use for traversal.</param>
        /// <param name="handlerFactory">Factory for creating counting handlers.</param>
        /// <param name="pathMapper">The path mapper for converting between system and canonical paths.</param>
        /// <param name="logger">Logger for diagnostic messages.</param>
        /// <param name="loggerFactory">Logger factory for creating additional loggers.</param>
        public FileCounterService(
            IFileSystem fileSystem,
            HandlerFactory handlerFactory,
            IPathMapper pathMapper,
            ILogger<FileCounterService> logger,
            ILoggerFactory loggerFactory)
        {
            _fileSystem = fileSystem;
            _handlerFactory = handlerFactory;
            _pathMapper = pathMapper;
            _logger = logger;
            _loggerFactory = loggerFactory;
        }

        /// <summary>
        /// Executes a file counting operation with the specified options.
        /// </summary>
        /// <param name="options">The counting options.</param>
        /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
        /// <returns>A result containing the counting handler with results or an error.</returns>
        public async Task<Result<ICountingHandler, FileSystemError>> CountAsync(
            FileCounter.Models.FileCounterOptions options,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Starting file count in: {Path}", options.Path);

                // Convert to canonical path using the proper path mapper
                var absolutePath = Path.GetFullPath(options.Path);
                _logger.LogInformation("Absolute path resolved to: {AbsolutePath}", absolutePath);
                
                // Check if path exists and list contents
                if (Directory.Exists(absolutePath))
                {
                    try
                    {
                        var entries = Directory.GetFileSystemEntries(absolutePath);
                        _logger.LogInformation("Directory contains {Count} entries", entries.Length);
                        if (_logger.IsEnabled(LogLevel.Debug))
                        {
                            foreach (var entry in entries.Take(10))
                            {
                                _logger.LogDebug("Found entry: {Entry}", entry);
                            }
                            if (entries.Length > 10)
                            {
                                _logger.LogDebug("... and {RemainingCount} more entries", entries.Length - 10);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Error listing directory contents: {Error}", ex.Message);
                    }
                }
                
                var canonicalResult = _pathMapper.MapToCanonicalPath(absolutePath);
                if (canonicalResult.IsFailure)
                {
                    _logger.LogError("Invalid path: {Error}", canonicalResult.Error.Message);
                    return Result<ICountingHandler, FileSystemError>.Failure(
                        new FileSystemError("InvalidPath", canonicalResult.Error.Message));
                }

                _logger.LogInformation("Canonical path resolved to: {CanonicalPath}", canonicalResult.Value.Value);

                // Create handler and mapper
                var handler = _handlerFactory.CreateHandler(options.HandlerType, options);
                
                // Add progress reporter if verbose or if operation might be long
                if (options.Verbose || ShouldShowProgress(options.Path))
                {
                    var progressReporter = new ConsoleProgressReporter(
                        _loggerFactory.CreateLogger<ConsoleProgressReporter>(),
                        options.Verbose);
                    handler.ProgressReporter = progressReporter;
                }
                
                var mapper = new IdentityMapper();

                // Configure traversal options
                var maxDepth = options.MaxDepth ?? (options.Recursive ? null : 1);
                var traversalOptions = new TraversalOptions(
                    maxDepth: maxDepth,
                    recursive: options.Recursive,
                    strategyHint: options.Strategy);

                // Perform traversal
                var result = await _fileSystem.TraverseAsync(
                    canonicalResult.Value,
                    handler,
                    mapper,
                    traversalOptions,
                    cancellationToken);

                if (result.IsSuccess)
                {
                    var message = handler.ErrorCount > 0 
                        ? $"File counting completed with {handler.ErrorCount} errors (access denied, etc.)"
                        : "File counting completed successfully";
                    _logger.LogInformation(message);
                    return Result<ICountingHandler, FileSystemError>.Success(handler);
                }
                else
                {
                    // Check if this is an access denied error and we still got some results
                    if (result.Error.Type == FileSystemErrorType.AccessDenied && (handler.FileCount > 0 || handler.DirectoryCount > 0))
                    {
                        _logger.LogWarning("Traversal completed with access denied error, but partial results available: {Error}", result.Error.Message);
                        return Result<ICountingHandler, FileSystemError>.Success(handler);
                    }
                    
                    _logger.LogError("Traversal failed: {Error}", result.Error.Message);
                    return Result<ICountingHandler, FileSystemError>.Failure(result.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during file counting");
                return Result<ICountingHandler, FileSystemError>.Failure(
                    new FileSystemError("UnexpectedError", ex.Message));
            }
        }

        /// <summary>
        /// Executes a file counting operation and exports the results.
        /// </summary>
        /// <param name="options">The counting options.</param>
        /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
        /// <returns>A result containing the exported content or an error.</returns>
        public async Task<Result<string, FileSystemError>> CountAndExportAsync(
            FileCounter.Models.FileCounterOptions options,
            CancellationToken cancellationToken = default)
        {
            var countResult = await CountAsync(options, cancellationToken);
            if (countResult.IsFailure)
            {
                return Result<string, FileSystemError>.Failure(countResult.Error);
            }

            try
            {
                var content = await CounterExporter.ExportAsync(
                    countResult.Value,
                    options.ExportFormat,
                    options.OutputPath);

                return Result<string, FileSystemError>.Success(content ?? "Results exported to file");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during export");
                return Result<string, FileSystemError>.Failure(
                    new FileSystemError("ExportError", ex.Message));
            }
        }

        /// <summary>
        /// Determines if progress reporting should be shown for the given path.
        /// </summary>
        /// <param name="path">The path being processed.</param>
        /// <returns>True if progress should be shown, false otherwise.</returns>
        private bool ShouldShowProgress(string path)
        {
            // Show progress for network paths or large directories
            return path.StartsWith("\\\\") || IsLargeDirectory(path);
        }

        /// <summary>
        /// Determines if a directory is likely to be large based on simple heuristics.
        /// </summary>
        /// <param name="path">The directory path to check.</param>
        /// <returns>True if the directory is likely to be large, false otherwise.</returns>
        private bool IsLargeDirectory(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                    return false;
                    
                // Quick check: if we can enumerate more than 1000 entries, it's probably large
                var entries = Directory.EnumerateFileSystemEntries(path);
                return entries.Take(1001).Count() > 1000;
            }
            catch
            {
                // If we can't check, err on the side of showing progress
                return true;
            }
        }

    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.FileSystem.Traversal.Strategies;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Local
{
    /// <summary>
    /// Local filesystem implementation of <see cref="IFileSystem"/> for cross-platform file operations.
    /// </summary>
    public class LocalFileSystem : IFileSystem
    {
        private readonly IPathMapper _pathMapper;
        
        /// <summary>
        /// Initializes a new instance of the LocalFileSystem class.
        /// </summary>
        /// <param name="pathMapper">Path mapper for canonical path support.</param>
        public LocalFileSystem(IPathMapper pathMapper)
        {
            _pathMapper = pathMapper ?? throw new ArgumentNullException(nameof(pathMapper));
        }

        /// <inheritdoc />
        public FileSystemInfo Info { get; } = new FileSystemInfo(
            isDurable: true,           // Local file system persists data
            supportsMonitoring: true,  // Supports FileSystemWatcher
            isReadOnly: false          // Supports read and write operations
        );

        /// <inheritdoc />
        public Task<Result<bool, FileSystemError>> FileExistsAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<bool, FileSystemError>.Failure(
                        new FileSystemError(systemPathResult.Error.Message, path.Value, FileSystemErrorType.InvalidPath)));

                var systemPath = systemPathResult.Value;
                var exists = File.Exists(systemPath);
                return Task.FromResult(Result<bool, FileSystemError>.Success(exists));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<bool, FileSystemError>.Failure(
                    FileSystemError.FromException(ex, path.Value)));
            }
        }

        /// <inheritdoc />
        public Task<Result<bool, FileSystemError>> DirectoryExistsAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<bool, FileSystemError>.Failure(
                        new FileSystemError(systemPathResult.Error.Message, path.Value, FileSystemErrorType.InvalidPath)));

                var systemPath = systemPathResult.Value;
                var exists = Directory.Exists(systemPath);
                return Task.FromResult(Result<bool, FileSystemError>.Success(exists));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<bool, FileSystemError>.Failure(
                    FileSystemError.FromException(ex, path.Value)));
            }
        }

        /// <inheritdoc />
        public Task<Result<string, FileSystemError>> ReadAllTextAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(async () =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Result<string, FileSystemError>.Failure(
                        new FileSystemError(systemPathResult.Error.Message, path.Value, FileSystemErrorType.InvalidPath));

                var systemPath = systemPathResult.Value;
                var content = await File.ReadAllTextAsync(systemPath, cancellationToken);
                return Result<string, FileSystemError>.Success(content);
            }, path.Value);
        }

        /// <inheritdoc />
        public Task<Result<Stream, FileSystemError>> OpenReadAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<Stream, FileSystemError>.Failure(
                        new FileSystemError(systemPathResult.Error.Message, path.Value, FileSystemErrorType.InvalidPath)));

                cancellationToken.ThrowIfCancellationRequested();
                var systemPath = systemPathResult.Value;
                var stream = File.OpenRead(systemPath);
                return Task.FromResult(Result<Stream, FileSystemError>.Success(stream));
            }, path.Value);
        }

        /// <inheritdoc />
        public Task<Result<FileSystemEntry, FileSystemError>> GetEntryAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<FileSystemEntry, FileSystemError>.Failure(
                        new FileSystemError(systemPathResult.Error.Message, path.Value, FileSystemErrorType.InvalidPath)));

                cancellationToken.ThrowIfCancellationRequested();
                
                var systemPath = systemPathResult.Value;
                
                // Check if it's a file
                if (File.Exists(systemPath))
                {
                    var fileInfo = new FileInfo(systemPath);
                    var entry = fileInfo.ToFileSystemEntry(_pathMapper);
                    return Task.FromResult(Result<FileSystemEntry, FileSystemError>.Success(entry));
                }
                
                // Check if it's a directory
                if (Directory.Exists(systemPath))
                {
                    var directoryInfo = new DirectoryInfo(systemPath);
                    var entry = directoryInfo.ToFileSystemEntry(_pathMapper);
                    return Task.FromResult(Result<FileSystemEntry, FileSystemError>.Success(entry));
                }
                
                return Task.FromResult(Result<FileSystemEntry, FileSystemError>.Failure(
                    new FileSystemError($"Path not found: {path.Value}", path.Value, FileSystemErrorType.PathNotFound)));
            }, path.Value);
        }

        /// <inheritdoc />
        public Task<Result<IReadOnlyList<CanonicalPath>, FileSystemError>> GetFilesAsync(CanonicalPath path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<IReadOnlyList<CanonicalPath>, FileSystemError>.Failure(
                        new FileSystemError(systemPathResult.Error.Message, path.Value, FileSystemErrorType.InvalidPath)));

                cancellationToken.ThrowIfCancellationRequested();
                
                var systemPath = systemPathResult.Value;
                var files = Directory.GetFiles(systemPath, searchPattern, searchOption);
                var mappedFiles = MapPathsToCanonical(files);
                return Task.FromResult(Result<IReadOnlyList<CanonicalPath>, FileSystemError>.Success(mappedFiles));
            }, path.Value);
        }

        /// <inheritdoc />
        public Task<Result<IReadOnlyList<CanonicalPath>, FileSystemError>> GetDirectoriesAsync(CanonicalPath path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<IReadOnlyList<CanonicalPath>, FileSystemError>.Failure(
                        new FileSystemError(systemPathResult.Error.Message, path.Value, FileSystemErrorType.InvalidPath)));

                cancellationToken.ThrowIfCancellationRequested();
                
                var systemPath = systemPathResult.Value;
                var directories = Directory.GetDirectories(systemPath, searchPattern, searchOption);
                var mappedDirectories = MapPathsToCanonical(directories);
                return Task.FromResult(Result<IReadOnlyList<CanonicalPath>, FileSystemError>.Success(mappedDirectories));
            }, path.Value);
        }


        /// <inheritdoc />
        public Task<Result<Unit, FileSystemError>> CreateDirectoryAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                        new FileSystemError(systemPathResult.Error.Message, path.Value, FileSystemErrorType.InvalidPath)));

                cancellationToken.ThrowIfCancellationRequested();
                var systemPath = systemPathResult.Value;
                Directory.CreateDirectory(systemPath);
                return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
            }, path.Value);
        }

        /// <inheritdoc />
        public Task<Result<Unit, FileSystemError>> WriteAllTextAsync(CanonicalPath path, string contents, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(async () =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError(systemPathResult.Error.Message, path.Value, FileSystemErrorType.InvalidPath));

                if (contents == null)
                    contents = string.Empty;

                var systemPath = systemPathResult.Value;
                await File.WriteAllTextAsync(systemPath, contents, cancellationToken);
                return Result<Unit, FileSystemError>.Success(Unit.Value);
            }, path.Value);
        }

        /// <inheritdoc />
        public Task<Result<Stream, FileSystemError>> OpenWriteAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<Stream, FileSystemError>.Failure(
                        new FileSystemError(systemPathResult.Error.Message, path.Value, FileSystemErrorType.InvalidPath)));

                cancellationToken.ThrowIfCancellationRequested();
                var systemPath = systemPathResult.Value;
                var stream = File.OpenWrite(systemPath);
                return Task.FromResult(Result<Stream, FileSystemError>.Success(stream));
            }, path.Value);
        }

        /// <inheritdoc />
        public Task<Result<Stream, FileSystemError>> OpenAsync(CanonicalPath path, FileMode mode, FileAccess access, FileShare share, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<Stream, FileSystemError>.Failure(
                        new FileSystemError(systemPathResult.Error.Message, path.Value, FileSystemErrorType.InvalidPath)));

                cancellationToken.ThrowIfCancellationRequested();
                var systemPath = systemPathResult.Value;
                var stream = new FileStream(systemPath, mode, access, share);
                return Task.FromResult(Result<Stream, FileSystemError>.Success(stream));
            }, path.Value);
        }

        /// <inheritdoc />
        public Task<Result<Unit, FileSystemError>> DeleteFileAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                        new FileSystemError(systemPathResult.Error.Message, path.Value, FileSystemErrorType.InvalidPath)));

                cancellationToken.ThrowIfCancellationRequested();
                var systemPath = systemPathResult.Value;
                File.Delete(systemPath);
                return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
            }, path.Value);
        }

        /// <inheritdoc />
        public Task<Result<Unit, FileSystemError>> DeleteDirectoryAsync(CanonicalPath path, bool recursive = false, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                        new FileSystemError(systemPathResult.Error.Message, path.Value, FileSystemErrorType.InvalidPath)));

                cancellationToken.ThrowIfCancellationRequested();
                var systemPath = systemPathResult.Value;
                Directory.Delete(systemPath, recursive);
                return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
            }, path.Value);
        }

        /// <inheritdoc />
        public async Task<Result<TraversalSummary, FileSystemError>> TraverseAsync(
            CanonicalPath path,
            ITraversalHandler handler,
            TraversalOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteAsync(async () =>
            {
                options ??= new TraversalOptions();
                
                // Notify handler of traversal start
                var startResult = await handler.OnTraversalStartAsync(path, cancellationToken);
                if (startResult.IsFailure)
                    return Result<TraversalSummary, FileSystemError>.Failure(startResult.Error);

                var context = TraversalContext.CreateInitial(path);
                var stats = new TraversalStats();
                
                // Get the starting entry
                var entryResult = await GetEntryAsync(path, cancellationToken);
                if (entryResult.IsFailure)
                    return Result<TraversalSummary, FileSystemError>.Failure(entryResult.Error);

                // Select strategy based on hint
                var strategy = CreateStrategy(options.StrategyHint);
                
                // Perform traversal
                var traversalResult = await strategy.TraverseAsync(
                    entryResult.Value, 
                    handler, 
                    options, 
                    context, 
                    stats,
                    GetChildrenAsync,
                    cancellationToken);

                if (traversalResult.IsFailure)
                    return Result<TraversalSummary, FileSystemError>.Failure(traversalResult.Error);

                var summary = new TraversalSummary(
                    stats.FilesProcessed,
                    stats.DirectoriesProcessed,
                    stats.TotalBytesProcessed,
                    DateTime.UtcNow - context.StartTimeUtc,
                    cancellationToken.IsCancellationRequested);

                // Notify handler of completion
                var completeResult = await handler.OnTraversalCompleteAsync(summary, cancellationToken);
                if (completeResult.IsFailure)
                    return Result<TraversalSummary, FileSystemError>.Failure(completeResult.Error);

                return Result<TraversalSummary, FileSystemError>.Success(summary);
            }, path.Value);
        }

        /// <summary>
        /// Creates a traversal strategy based on the provided hint.
        /// </summary>
        /// <param name="hint">The strategy hint.</param>
        /// <returns>An appropriate traversal strategy.</returns>
        private ITraversalStrategy CreateStrategy(TraversalStrategyHint hint)
        {
            return hint switch
            {
                TraversalStrategyHint.Parallel => new ParallelTraversalStrategy(),
                TraversalStrategyHint.MemoryEfficient => new SequentialTraversalStrategy(),
                TraversalStrategyHint.Search => new SequentialTraversalStrategy(),
                _ => new SequentialTraversalStrategy()
            };
        }

        /// <summary>
        /// Gets the children of a directory for traversal.
        /// </summary>
        /// <param name="path">The directory path.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the children or an error.</returns>
        private Task<Result<IEnumerable<FileSystemEntry>, FileSystemError>> GetChildrenAsync(
            CanonicalPath path, 
            CancellationToken cancellationToken)
        {
            try
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<IEnumerable<FileSystemEntry>, FileSystemError>.Failure(
                        new FileSystemError(systemPathResult.Error.Message, path.Value, FileSystemErrorType.InvalidPath)));

                var systemPath = systemPathResult.Value;
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(systemPath))
                    return Task.FromResult(Result<IEnumerable<FileSystemEntry>, FileSystemError>.Failure(
                        new FileSystemError($"Directory not found: {path.Value}", path.Value, FileSystemErrorType.DirectoryNotFound)));

                var entries = new List<FileSystemEntry>();

                // Get directories
                var directories = Directory.GetDirectories(systemPath);
                foreach (var dir in directories)
                {
                    var canonicalDirResult = MapPathToCanonical(dir);
                    if (canonicalDirResult.IsSuccess)
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        var entry = new FileSystemEntry(
                            canonicalDirResult.Value,
                            true,
                            null,
                            null,
                            dirInfo.CreationTimeUtc,
                            dirInfo.LastWriteTimeUtc,
                            dirInfo.LastAccessTimeUtc,
                            dirInfo.Attributes);
                        entries.Add(entry);
                    }
                }

                // Get files
                var files = Directory.GetFiles(systemPath);
                foreach (var file in files)
                {
                    var canonicalFileResult = MapPathToCanonical(file);
                    if (canonicalFileResult.IsSuccess)
                    {
                        var fileInfo = new FileInfo(file);
                        var entry = new FileSystemEntry(
                            canonicalFileResult.Value,
                            false,
                            fileInfo.Length,
                            Path.GetExtension(file),
                            fileInfo.CreationTimeUtc,
                            fileInfo.LastWriteTimeUtc,
                            fileInfo.LastAccessTimeUtc,
                            fileInfo.Attributes);
                        entries.Add(entry);
                    }
                }

                return Task.FromResult(Result<IEnumerable<FileSystemEntry>, FileSystemError>.Success(entries));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<IEnumerable<FileSystemEntry>, FileSystemError>.Failure(
                    FileSystemError.FromException(ex, path.Value)));
            }
        }

        /// <inheritdoc />
        public Task<Result<IMonitoringSession, FileSystemError>> CreateMonitoringSessionAsync(MonitoringOptions options, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (options == null)
                {
                    return Task.FromResult(Result<IMonitoringSession, FileSystemError>.Failure(
                        new FileSystemError("Monitoring options cannot be null", null, FileSystemErrorType.Unknown)));
                }

                if (_pathMapper == null)
                {
                    return Task.FromResult(Result<IMonitoringSession, FileSystemError>.Failure(
                        new FileSystemError("Path mapper is required for monitoring operations", options.Path.Value, FileSystemErrorType.Unknown)));
                }

                // Convert canonical path to system path for monitoring
                var systemPathResult = GetSystemPath(options.Path);
                if (systemPathResult.IsFailure)
                {
                    return Task.FromResult(Result<IMonitoringSession, FileSystemError>.Failure(
                        new FileSystemError(systemPathResult.Error.Message, options.Path.Value, FileSystemErrorType.InvalidPath)));
                }

                var systemPath = systemPathResult.Value;
                if (!Directory.Exists(systemPath))
                {
                    return Task.FromResult(Result<IMonitoringSession, FileSystemError>.Failure(
                        new FileSystemError($"Directory not found: {options.Path.Value}", options.Path.Value, FileSystemErrorType.DirectoryNotFound)));
                }

                var session = new MonitoringSession(options, _pathMapper);
                return Task.FromResult(Result<IMonitoringSession, FileSystemError>.Success(session));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<IMonitoringSession, FileSystemError>.Failure(
                    FileSystemError.FromException(ex, options?.Path.Value)));
            }
        }

        private async Task<Result<T, FileSystemError>> ExecuteAsync<T>(Func<Task<Result<T, FileSystemError>>> operation, string? path = null)
        {
            try
            {
                return await operation();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result<T, FileSystemError>.Failure(FileSystemError.FromException(ex, path));
            }
        }


        /// <summary>
        /// Gets the system path from a canonical path, applying mapping if available.
        /// </summary>
        /// <param name="canonicalPath">The canonical path to map.</param>
        /// <returns>A result containing the system path or an error.</returns>
        private Result<string, Error> GetSystemPath(CanonicalPath canonicalPath)
        {
            if (_pathMapper != null)
            {
                return _pathMapper.MapToSystemPath(canonicalPath);
            }
            
            // Return canonical path value if no mapper is available
            return Result<string, Error>.Success(canonicalPath.Value);
        }
        
        /// <summary>
        /// Maps an array of system paths back to canonical format.
        /// </summary>
        /// <param name="systemPaths">Array of system paths.</param>
        /// <returns>List of canonical paths.</returns>
        private IReadOnlyList<CanonicalPath> MapPathsToCanonical(string[] systemPaths)
        {
            var canonicalPaths = new List<CanonicalPath>();
            
            foreach (var systemPath in systemPaths)
            {
                var result = MapPathToCanonical(systemPath);
                if (result.IsSuccess)
                {
                    canonicalPaths.Add(result.Value);
                }
                else
                {
                    // Fallback: try to create canonical path from system path
                    var fallbackResult = CanonicalPath.Create(systemPath.Replace('\\', '/'));
                    if (fallbackResult.IsSuccess)
                    {
                        canonicalPaths.Add(fallbackResult.Value);
                    }
                }
            }
            
            return canonicalPaths;
        }
        
        /// <summary>
        /// Maps a system path back to canonical format.
        /// </summary>
        /// <param name="systemPath">The system path to map.</param>
        /// <returns>Result containing the canonical path or an error.</returns>
        private Result<CanonicalPath, Error> MapPathToCanonical(string systemPath)
        {
            if (_pathMapper != null)
            {
                return _pathMapper.MapToCanonicalPath(systemPath);
            }
            
            // Fallback: try to create canonical path from system path
            var normalizedPath = systemPath.Replace('\\', '/');
            if (!normalizedPath.StartsWith('/'))
            {
                normalizedPath = "/" + normalizedPath;
            }
            
            return CanonicalPath.Create(normalizedPath);
        }

        /// <summary>
        /// Normalizes a path for file system operations.
        /// </summary>
        /// <param name="path">The path to normalize.</param>
        /// <returns>The normalized path.</returns>
        internal string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
            
            return Path.GetFullPath(path);
        }
    }
}
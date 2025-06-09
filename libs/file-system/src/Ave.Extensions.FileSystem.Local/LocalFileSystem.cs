using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Mapping;
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

        private Task<Result<FileSystemEntry, FileSystemError>> GetEntryInternalAsync(CanonicalPath path, CancellationToken cancellationToken = default)
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
                    var fileInfo = new System.IO.FileInfo(systemPath);
                    var entry = fileInfo.ToFileSystemEntry(_pathMapper);
                    return Task.FromResult(Result<FileSystemEntry, FileSystemError>.Success(entry));
                }
                
                // Check if it's a directory
                if (Directory.Exists(systemPath))
                {
                    var directoryInfo = new System.IO.DirectoryInfo(systemPath);
                    var entry = directoryInfo.ToFileSystemEntry(_pathMapper);
                    return Task.FromResult(Result<FileSystemEntry, FileSystemError>.Success(entry));
                }
                
                return Task.FromResult(Result<FileSystemEntry, FileSystemError>.Failure(
                    new FileSystemError($"Path not found: {path.Value}", path.Value, FileSystemErrorType.PathNotFound)));
            }, path.Value);
        }

        /// <inheritdoc />
        public async Task<Result<TEntry, FileSystemError>> GetEntryAsync<TEntry>(
            CanonicalPath path,
            IFileSystemEntryMapper<TEntry> mapper,
            CancellationToken cancellationToken = default)
        {
            if (mapper == null)
                throw new ArgumentNullException(nameof(mapper));
            
            try
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Result<TEntry, FileSystemError>.Failure(
                        new FileSystemError(systemPathResult.Error.Message, path.Value, FileSystemErrorType.InvalidPath));

                var systemPath = systemPathResult.Value;
                
                // Check if it's a file
                if (File.Exists(systemPath))
                {
                    var fileInfo = new System.IO.FileInfo(systemPath);
                    
                    // Create EntryInfo
                    var entryInfo = new Models.EntryInfo(
                        fileInfo.Name,
                        fileInfo.CreationTimeUtc,
                        fileInfo.LastWriteTimeUtc,
                        fileInfo.LastAccessTimeUtc,
                        fileInfo.Attributes);
                    
                    // Create FileInfo
                    var fileInfoModel = new Models.FileInfo(entryInfo, fileInfo.Length, fileInfo.Extension);
                    
                    // Create FileEntry
                    var fileEntry = new Models.FileEntry(path, fileInfoModel);
                    
                    // Create file reader for this specific file
                    var fileReader = new LocalFileReader(this, path);
                    return await mapper.MapFileAsync(fileEntry, fileReader, cancellationToken);
                }
                
                // Check if it's a directory
                if (Directory.Exists(systemPath))
                {
                    var directoryInfo = new System.IO.DirectoryInfo(systemPath);
                    
                    // Create EntryInfo
                    var entryInfo = new Models.EntryInfo(
                        directoryInfo.Name,
                        directoryInfo.CreationTimeUtc,
                        directoryInfo.LastWriteTimeUtc,
                        directoryInfo.LastAccessTimeUtc,
                        directoryInfo.Attributes);
                    
                    // Create DirectoryInfo
                    var directoryInfoModel = new Models.DirectoryInfo(entryInfo);
                    
                    // Create DirectoryEntry
                    var directoryEntry = new Models.DirectoryEntry(path, directoryInfoModel);
                    return await mapper.MapDirectoryAsync(directoryEntry, cancellationToken);
                }
                
                return Result<TEntry, FileSystemError>.Failure(
                    new FileSystemError($"Path not found: {path.Value}", path.Value, FileSystemErrorType.PathNotFound));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result<TEntry, FileSystemError>.Failure(
                    FileSystemError.FromException(ex, path.Value));
            }
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
        public async Task<Result<TraversalSummary, FileSystemError>> TraverseAsync<TEntry>(
            CanonicalPath path,
            ITraversalHandler<TEntry> handler,
            IFileSystemEntryMapper<TEntry> mapper,
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
                
                // Get the starting entry as IFileSystemEntry
                var entryResult = await GetStartingEntryAsync(path, cancellationToken);
                if (entryResult.IsFailure)
                    return Result<TraversalSummary, FileSystemError>.Failure(entryResult.Error);

                // Select strategy based on hint and provide file reader factory
                var strategy = CreateLocalStrategy(options.StrategyHint);
                
                // Perform traversal
                var traversalResult = await strategy.TraverseAsync(
                    entryResult.Value, 
                    handler,
                    mapper,
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
            switch (hint)
            {
                case TraversalStrategyHint.Parallel:
                    return new ParallelTraversalStrategy();
                case TraversalStrategyHint.MemoryEfficient:
                    return new SequentialTraversalStrategy();
                case TraversalStrategyHint.Search:
                    return new SequentialTraversalStrategy();
                default:
                    return new SequentialTraversalStrategy();
            }
        }

        private ITraversalStrategy CreateLocalStrategy(TraversalStrategyHint hint)
        {
            switch (hint)
            {
                case TraversalStrategyHint.Parallel:
                    return new LocalParallelTraversalStrategy(this);
                case TraversalStrategyHint.MemoryEfficient:
                    return new LocalSequentialTraversalStrategy(this);
                case TraversalStrategyHint.Search:
                    return new LocalSequentialTraversalStrategy(this);
                default:
                    return new LocalSequentialTraversalStrategy(this);
            }
        }

        /// <summary>
        /// Gets a single entry as IFileSystemEntry for traversal starting point.
        /// </summary>
        /// <param name="path">The path to get the entry for.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the entry or an error.</returns>
        private Task<Result<IFileSystemEntry, FileSystemError>> GetStartingEntryAsync(
            CanonicalPath path, 
            CancellationToken cancellationToken)
        {
            try
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<IFileSystemEntry, FileSystemError>.Failure(
                        new FileSystemError(systemPathResult.Error.Message, path.Value, FileSystemErrorType.InvalidPath)));

                var systemPath = systemPathResult.Value;
                
                // Check if it's a file
                if (File.Exists(systemPath))
                {
                    var fileInfo = new System.IO.FileInfo(systemPath);
                    var entryInfo = new Models.EntryInfo(
                        fileInfo.Name,
                        fileInfo.CreationTimeUtc,
                        fileInfo.LastWriteTimeUtc,
                        fileInfo.LastAccessTimeUtc,
                        fileInfo.Attributes);
                    
                    var fileInfoModel = new Models.FileInfo(entryInfo, fileInfo.Length, fileInfo.Extension);
                    IFileSystemEntry entry = new Models.FileEntry(path, fileInfoModel);
                    return Task.FromResult(Result<IFileSystemEntry, FileSystemError>.Success(entry));
                }
                
                // Check if it's a directory
                if (Directory.Exists(systemPath))
                {
                    var directoryInfo = new System.IO.DirectoryInfo(systemPath);
                    var entryInfo = new Models.EntryInfo(
                        directoryInfo.Name,
                        directoryInfo.CreationTimeUtc,
                        directoryInfo.LastWriteTimeUtc,
                        directoryInfo.LastAccessTimeUtc,
                        directoryInfo.Attributes);
                    
                    var directoryInfoModel = new Models.DirectoryInfo(entryInfo);
                    IFileSystemEntry entry = new Models.DirectoryEntry(path, directoryInfoModel);
                    return Task.FromResult(Result<IFileSystemEntry, FileSystemError>.Success(entry));
                }
                
                return Task.FromResult(Result<IFileSystemEntry, FileSystemError>.Failure(
                    new FileSystemError($"Path not found: {path.Value}", path.Value, FileSystemErrorType.PathNotFound)));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<IFileSystemEntry, FileSystemError>.Failure(
                    FileSystemError.FromException(ex, path.Value)));
            }
        }

        /// <summary>
        /// Gets the children of a directory for traversal.
        /// </summary>
        /// <param name="path">The directory path.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the children or an error.</returns>
        private Task<Result<IEnumerable<IFileSystemEntry>, FileSystemError>> GetChildrenAsync(
            CanonicalPath path, 
            CancellationToken cancellationToken)
        {
            try
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<IEnumerable<IFileSystemEntry>, FileSystemError>.Failure(
                        new FileSystemError(systemPathResult.Error.Message, path.Value, FileSystemErrorType.InvalidPath)));

                var systemPath = systemPathResult.Value;
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(systemPath))
                    return Task.FromResult(Result<IEnumerable<IFileSystemEntry>, FileSystemError>.Failure(
                        new FileSystemError($"Directory not found: {path.Value}", path.Value, FileSystemErrorType.DirectoryNotFound)));

                var entries = new List<IFileSystemEntry>();

                // Get directories
                var directories = Directory.GetDirectories(systemPath);
                foreach (var dir in directories)
                {
                    var canonicalDirResult = MapPathToCanonical(dir);
                    if (canonicalDirResult.IsSuccess)
                    {
                        var dirInfo = new System.IO.DirectoryInfo(dir);
                        var entryInfo = new Models.EntryInfo(
                            dirInfo.Name,
                            dirInfo.CreationTimeUtc,
                            dirInfo.LastWriteTimeUtc,
                            dirInfo.LastAccessTimeUtc,
                            dirInfo.Attributes);
                        
                        var directoryInfoModel = new Models.DirectoryInfo(entryInfo);
                        entries.Add(new Models.DirectoryEntry(canonicalDirResult.Value, directoryInfoModel));
                    }
                }

                // Get files
                var files = Directory.GetFiles(systemPath);
                foreach (var file in files)
                {
                    var canonicalFileResult = MapPathToCanonical(file);
                    if (canonicalFileResult.IsSuccess)
                    {
                        var fileInfo = new System.IO.FileInfo(file);
                        var entryInfo = new Models.EntryInfo(
                            fileInfo.Name,
                            fileInfo.CreationTimeUtc,
                            fileInfo.LastWriteTimeUtc,
                            fileInfo.LastAccessTimeUtc,
                            fileInfo.Attributes);
                        
                        var fileInfoModel = new Models.FileInfo(entryInfo, fileInfo.Length, fileInfo.Extension);
                        entries.Add(new Models.FileEntry(canonicalFileResult.Value, fileInfoModel));
                    }
                }

                return Task.FromResult(Result<IEnumerable<IFileSystemEntry>, FileSystemError>.Success(entries));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<IEnumerable<IFileSystemEntry>, FileSystemError>.Failure(
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

        /// <inheritdoc />
        public Task<Result<Unit, FileSystemError>> MoveFileAsync(
            CanonicalPath source, 
            CanonicalPath destination, 
            bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate paths are different
                var validationResult = ValidateSourceDestinationDifferent(source, destination);
                if (validationResult.IsFailure)
                    return Task.FromResult(validationResult);

                // Get system paths
                var sourcePathResult = GetSystemPath(source);
                if (sourcePathResult.IsFailure)
                    return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                        new FileSystemError(sourcePathResult.Error.Message, source.Value, FileSystemErrorType.InvalidPath)));
                        
                var destPathResult = GetSystemPath(destination);
                if (destPathResult.IsFailure)
                    return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                        new FileSystemError(destPathResult.Error.Message, destination.Value, FileSystemErrorType.InvalidPath)));
                
                var sourcePath = sourcePathResult.Value;
                var destPath = destPathResult.Value;
                
                cancellationToken.ThrowIfCancellationRequested();
                
                // Check if source exists
                if (!File.Exists(sourcePath))
                    return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Source file not found", source.Value, FileSystemErrorType.FileNotFound)));
                
                // Check if destination exists and overwrite is false
                if (!overwrite && File.Exists(destPath))
                    return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Destination file already exists", destination.Value, FileSystemErrorType.FileAlreadyExists)));
                
                // Ensure destination directory exists
                var ensureDirResult = EnsureParentDirectoryExists(destPath);
                if (ensureDirResult.IsFailure)
                    return Task.FromResult(ensureDirResult);
                
                // Get source file info for attribute preservation
                var sourceInfo = new System.IO.FileInfo(sourcePath);
                var sourceAttributes = sourceInfo.Attributes;
                var sourceCreationTime = sourceInfo.CreationTimeUtc;
                var sourceLastWriteTime = sourceInfo.LastWriteTimeUtc;
                var sourceLastAccessTime = sourceInfo.LastAccessTimeUtc;
                
                // Clear readonly attribute if set (to allow move)
                if ((sourceAttributes & FileAttributes.ReadOnly) != 0)
                {
                    sourceInfo.Attributes = sourceAttributes & ~FileAttributes.ReadOnly;
                }
                
                cancellationToken.ThrowIfCancellationRequested();
                
                // Perform the move operation
                // For older frameworks, manually handle overwrite
                if (overwrite && File.Exists(destPath))
                {
                    File.Delete(destPath);
                }
                File.Move(sourcePath, destPath);
                
                // Preserve attributes on the destination
                // Note: Timestamps are automatically preserved by File.Move on most systems
                // but we ensure it here for consistency
                try
                {
                    var destInfo = new System.IO.FileInfo(destPath);
                    destInfo.CreationTimeUtc = sourceCreationTime;
                    destInfo.LastWriteTimeUtc = sourceLastWriteTime;
                    destInfo.LastAccessTimeUtc = sourceLastAccessTime;
                    destInfo.Attributes = sourceAttributes;
                }
                catch
                {
                    // Best effort - don't fail if attribute preservation fails
                }
                
                return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (UnauthorizedAccessException ex)
            {
                return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                    new FileSystemError(ex.Message, source.Value, FileSystemErrorType.AccessDenied)));
            }
            catch (DirectoryNotFoundException ex)
            {
                return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                    new FileSystemError(ex.Message, source.Value, FileSystemErrorType.DirectoryNotFound)));
            }
            catch (IOException ex) when (ex.HResult == -2147024891) // ERROR_ACCESS_DENIED on Windows
            {
                return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                    new FileSystemError(ex.Message, source.Value, FileSystemErrorType.AccessDenied)));
            }
            catch (IOException ex)
            {
                // Check if this is a cross-volume move error
                if (ex.Message.Contains("different volume") || ex.Message.Contains("Invalid cross-device"))
                {
                    return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Cannot move file across volumes", source.Value, FileSystemErrorType.CrossVolumeMove)));
                }
                
                return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                    new FileSystemError(ex.Message, source.Value, FileSystemErrorType.IOException)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                    FileSystemError.FromException(ex, source.Value)));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Unit, FileSystemError>> MoveDirectoryAsync(
            CanonicalPath source, 
            CanonicalPath destination,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate paths are different
                var validationResult = ValidateSourceDestinationDifferent(source, destination);
                if (validationResult.IsFailure)
                    return validationResult;

                // Get system paths
                var sourcePathResult = GetSystemPath(source);
                if (sourcePathResult.IsFailure)
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError(sourcePathResult.Error.Message, source.Value, FileSystemErrorType.InvalidPath));
                        
                var destPathResult = GetSystemPath(destination);
                if (destPathResult.IsFailure)
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError(destPathResult.Error.Message, destination.Value, FileSystemErrorType.InvalidPath));
                
                var sourcePath = sourcePathResult.Value;
                var destPath = destPathResult.Value;
                
                cancellationToken.ThrowIfCancellationRequested();
                
                // Check if source exists
                if (!Directory.Exists(sourcePath))
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Source directory not found", source.Value, FileSystemErrorType.DirectoryNotFound));
                
                // Check if destination already exists
                if (Directory.Exists(destPath))
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Destination directory already exists", destination.Value, FileSystemErrorType.DirectoryAlreadyExists));
                
                // Check if destination is a subdirectory of source
                if (IsSubdirectoryOf(sourcePath, destPath))
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Cannot move directory to a subdirectory of itself", source.Value, FileSystemErrorType.InvalidPath));
                
                // Get source directory info for attribute preservation
                var sourceDirInfo = new System.IO.DirectoryInfo(sourcePath);
                var sourceAttributes = sourceDirInfo.Attributes;
                var sourceCreationTime = sourceDirInfo.CreationTimeUtc;
                var sourceLastWriteTime = sourceDirInfo.LastWriteTimeUtc;
                
                cancellationToken.ThrowIfCancellationRequested();
                
                // Try to move the directory
                try
                {
                    Directory.Move(sourcePath, destPath);
                    
                    // Preserve attributes
                    try
                    {
                        var destDirInfo = new System.IO.DirectoryInfo(destPath);
                        destDirInfo.Attributes = sourceAttributes;
                        destDirInfo.CreationTimeUtc = sourceCreationTime;
                        destDirInfo.LastWriteTimeUtc = sourceLastWriteTime;
                    }
                    catch
                    {
                        // Best effort
                    }
                    
                    return Result<Unit, FileSystemError>.Success(Unit.Value);
                }
                catch (IOException ex) when (ex.Message.Contains("different volume") || ex.Message.Contains("Invalid cross-device"))
                {
                    // Cross-volume move: need to copy and delete
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    // Copy the directory
                    var copyResult = await CopyDirectoryRecursiveAsync(sourcePath, destPath, cancellationToken);
                    if (copyResult.IsFailure)
                        return copyResult;
                    
                    // Delete the source directory
                    try
                    {
                        Directory.Delete(sourcePath, recursive: true);
                        return Result<Unit, FileSystemError>.Success(Unit.Value);
                    }
                    catch (Exception deleteEx)
                    {
                        // Try to clean up the copy since delete failed
                        try
                        {
                            Directory.Delete(destPath, recursive: true);
                        }
                        catch
                        {
                            // Best effort
                        }
                        
                        return Result<Unit, FileSystemError>.Failure(
                            FileSystemError.FromException(deleteEx, source.Value));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (UnauthorizedAccessException ex)
            {
                return Result<Unit, FileSystemError>.Failure(
                    new FileSystemError(ex.Message, source.Value, FileSystemErrorType.AccessDenied));
            }
            catch (IOException ex)
            {
                return Result<Unit, FileSystemError>.Failure(
                    new FileSystemError(ex.Message, source.Value, FileSystemErrorType.IOException));
            }
            catch (Exception ex)
            {
                return Result<Unit, FileSystemError>.Failure(
                    FileSystemError.FromException(ex, source.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Unit, FileSystemError>> CopyFileAsync(
            CanonicalPath source, 
            CanonicalPath destination, 
            bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate paths are different
                var validationResult = ValidateSourceDestinationDifferent(source, destination);
                if (validationResult.IsFailure)
                    return validationResult;

                // Get system paths
                var sourcePathResult = GetSystemPath(source);
                if (sourcePathResult.IsFailure)
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError(sourcePathResult.Error.Message, source.Value, FileSystemErrorType.InvalidPath));
                        
                var destPathResult = GetSystemPath(destination);
                if (destPathResult.IsFailure)
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError(destPathResult.Error.Message, destination.Value, FileSystemErrorType.InvalidPath));
                
                var sourcePath = sourcePathResult.Value;
                var destPath = destPathResult.Value;
                
                cancellationToken.ThrowIfCancellationRequested();
                
                // Check if source exists
                if (!File.Exists(sourcePath))
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Source file not found", source.Value, FileSystemErrorType.FileNotFound));
                
                // Check if destination exists and overwrite is false
                if (!overwrite && File.Exists(destPath))
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Destination file already exists", destination.Value, FileSystemErrorType.FileAlreadyExists));
                
                // Ensure destination directory exists
                var ensureDirResult = EnsureParentDirectoryExists(destPath);
                if (ensureDirResult.IsFailure)
                    return ensureDirResult;
                
                // For async copy, we'll use FileStream for better cancellation support
                const int bufferSize = 81920; // 80KB buffer
                
                using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true))
                using (var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true))
                {
                    await sourceStream.CopyToAsync(destStream, bufferSize, cancellationToken);
                    await destStream.FlushAsync(cancellationToken);
                }
                
                // Preserve attributes and timestamps
                PreserveFileAttributes(sourcePath, destPath, preserveTimestamps: true, preserveAttributes: true);
                
                return Result<Unit, FileSystemError>.Success(Unit.Value);
            }
            catch (OperationCanceledException)
            {
                // Clean up partial copy
                try
                {
                    var destPathResult = GetSystemPath(destination);
                    if (destPathResult.IsSuccess && File.Exists(destPathResult.Value))
                    {
                        File.Delete(destPathResult.Value);
                    }
                }
                catch
                {
                    // Best effort cleanup
                }
                throw;
            }
            catch (UnauthorizedAccessException ex)
            {
                return Result<Unit, FileSystemError>.Failure(
                    new FileSystemError(ex.Message, source.Value, FileSystemErrorType.AccessDenied));
            }
            catch (DirectoryNotFoundException ex)
            {
                return Result<Unit, FileSystemError>.Failure(
                    new FileSystemError(ex.Message, source.Value, FileSystemErrorType.DirectoryNotFound));
            }
            catch (IOException ex) when (ex.HResult == -2147024784) // ERROR_DISK_FULL
            {
                // Clean up partial copy
                try
                {
                    var destPathResult = GetSystemPath(destination);
                    if (destPathResult.IsSuccess && File.Exists(destPathResult.Value))
                    {
                        File.Delete(destPathResult.Value);
                    }
                }
                catch
                {
                    // Best effort cleanup
                }
                
                return Result<Unit, FileSystemError>.Failure(
                    new FileSystemError("Insufficient disk space", destination.Value, FileSystemErrorType.InsufficientSpace));
            }
            catch (IOException ex)
            {
                return Result<Unit, FileSystemError>.Failure(
                    new FileSystemError(ex.Message, source.Value, FileSystemErrorType.IOException));
            }
            catch (Exception ex)
            {
                // Clean up partial copy on unexpected error
                try
                {
                    var destPathResult = GetSystemPath(destination);
                    if (destPathResult.IsSuccess && File.Exists(destPathResult.Value))
                    {
                        File.Delete(destPathResult.Value);
                    }
                }
                catch
                {
                    // Best effort cleanup
                }
                
                return Result<Unit, FileSystemError>.Failure(
                    FileSystemError.FromException(ex, source.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Unit, FileSystemError>> CopyDirectoryAsync(
            CanonicalPath source, 
            CanonicalPath destination,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate paths are different
                var validationResult = ValidateSourceDestinationDifferent(source, destination);
                if (validationResult.IsFailure)
                    return validationResult;

                // Get system paths
                var sourcePathResult = GetSystemPath(source);
                if (sourcePathResult.IsFailure)
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError(sourcePathResult.Error.Message, source.Value, FileSystemErrorType.InvalidPath));
                        
                var destPathResult = GetSystemPath(destination);
                if (destPathResult.IsFailure)
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError(destPathResult.Error.Message, destination.Value, FileSystemErrorType.InvalidPath));
                
                var sourcePath = sourcePathResult.Value;
                var destPath = destPathResult.Value;
                
                cancellationToken.ThrowIfCancellationRequested();
                
                // Check if source exists
                if (!Directory.Exists(sourcePath))
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Source directory not found", source.Value, FileSystemErrorType.DirectoryNotFound));
                
                // Check if destination already exists
                if (Directory.Exists(destPath))
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Destination directory already exists", destination.Value, FileSystemErrorType.DirectoryAlreadyExists));
                
                // Check if destination is a subdirectory of source
                if (IsSubdirectoryOf(sourcePath, destPath))
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Cannot copy directory to a subdirectory of itself", source.Value, FileSystemErrorType.InvalidPath));
                
                // Perform the recursive copy
                return await CopyDirectoryRecursiveAsync(sourcePath, destPath, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Clean up partial copy
                try
                {
                    var destPathResult = GetSystemPath(destination);
                    if (destPathResult.IsSuccess && Directory.Exists(destPathResult.Value))
                    {
                        Directory.Delete(destPathResult.Value, recursive: true);
                    }
                }
                catch
                {
                    // Best effort cleanup
                }
                throw;
            }
            catch (UnauthorizedAccessException ex)
            {
                return Result<Unit, FileSystemError>.Failure(
                    new FileSystemError(ex.Message, source.Value, FileSystemErrorType.AccessDenied));
            }
            catch (Exception ex)
            {
                return Result<Unit, FileSystemError>.Failure(
                    FileSystemError.FromException(ex, source.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Unit, FileSystemError>> MoveAsync(
            CanonicalPath source, 
            CanonicalPath destination, 
            bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Get system path to check if source is file or directory
                var sourcePathResult = GetSystemPath(source);
                if (sourcePathResult.IsFailure)
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError(sourcePathResult.Error.Message, source.Value, FileSystemErrorType.InvalidPath));
                
                var sourcePath = sourcePathResult.Value;
                
                // Check what type of entry we're dealing with
                if (File.Exists(sourcePath))
                {
                    // It's a file
                    return await MoveFileAsync(source, destination, overwrite, cancellationToken);
                }
                else if (Directory.Exists(sourcePath))
                {
                    // It's a directory - overwrite parameter doesn't apply to directories
                    if (overwrite)
                    {
                        // Check if destination exists and is a directory
                        var destPathResult = GetSystemPath(destination);
                        if (destPathResult.IsSuccess && Directory.Exists(destPathResult.Value))
                        {
                            return Result<Unit, FileSystemError>.Failure(
                                new FileSystemError(
                                    "Cannot overwrite an existing directory", 
                                    destination.Value, 
                                    FileSystemErrorType.DirectoryAlreadyExists));
                        }
                    }
                    return await MoveDirectoryAsync(source, destination, cancellationToken);
                }
                else
                {
                    // Source doesn't exist
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Source path not found", source.Value, FileSystemErrorType.PathNotFound));
                }
            }
            catch (Exception ex)
            {
                return Result<Unit, FileSystemError>.Failure(
                    FileSystemError.FromException(ex, source.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Unit, FileSystemError>> CopyAsync(
            CanonicalPath source, 
            CanonicalPath destination, 
            bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Get system path to check if source is file or directory
                var sourcePathResult = GetSystemPath(source);
                if (sourcePathResult.IsFailure)
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError(sourcePathResult.Error.Message, source.Value, FileSystemErrorType.InvalidPath));
                
                var sourcePath = sourcePathResult.Value;
                
                // Check what type of entry we're dealing with
                if (File.Exists(sourcePath))
                {
                    // It's a file
                    return await CopyFileAsync(source, destination, overwrite, cancellationToken);
                }
                else if (Directory.Exists(sourcePath))
                {
                    // It's a directory - overwrite parameter doesn't apply to directories
                    if (overwrite)
                    {
                        // Check if destination exists and is a directory
                        var destPathResult = GetSystemPath(destination);
                        if (destPathResult.IsSuccess && Directory.Exists(destPathResult.Value))
                        {
                            return Result<Unit, FileSystemError>.Failure(
                                new FileSystemError(
                                    "Cannot overwrite an existing directory", 
                                    destination.Value, 
                                    FileSystemErrorType.DirectoryAlreadyExists));
                        }
                    }
                    return await CopyDirectoryAsync(source, destination, cancellationToken);
                }
                else
                {
                    // Source doesn't exist
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Source path not found", source.Value, FileSystemErrorType.PathNotFound));
                }
            }
            catch (Exception ex)
            {
                return Result<Unit, FileSystemError>.Failure(
                    FileSystemError.FromException(ex, source.Value));
            }
        }

        #region Move/Copy Helper Methods

        /// <summary>
        /// Preserves file attributes from source to destination.
        /// </summary>
        private void PreserveFileAttributes(string sourcePath, string destinationPath, bool preserveTimestamps, bool preserveAttributes)
        {
            try
            {
                var sourceInfo = new System.IO.FileInfo(sourcePath);
                var destInfo = new System.IO.FileInfo(destinationPath);

                if (preserveTimestamps)
                {
                    // Preserve timestamps
                    destInfo.CreationTimeUtc = sourceInfo.CreationTimeUtc;
                    destInfo.LastWriteTimeUtc = sourceInfo.LastWriteTimeUtc;
                    destInfo.LastAccessTimeUtc = sourceInfo.LastAccessTimeUtc;
                }

                if (preserveAttributes)
                {
                    // Preserve all source attributes
                    // We need to be careful with ReadOnly as it can interfere with file operations
                    var sourceAttributes = sourceInfo.Attributes;
                    var hasReadOnly = (sourceAttributes & FileAttributes.ReadOnly) != 0;
                    
                    // First, set all attributes except ReadOnly
                    if (hasReadOnly)
                    {
                        destInfo.Attributes = sourceAttributes & ~FileAttributes.ReadOnly;
                    }
                    else
                    {
                        destInfo.Attributes = sourceAttributes;
                    }
                    
                    // Finally, if the source had ReadOnly, apply it
                    if (hasReadOnly)
                    {
                        destInfo.Attributes |= FileAttributes.ReadOnly;
                    }
                }
            }
            catch
            {
                // Best effort - don't fail the operation if attribute preservation fails
                // This is consistent with standard file system behavior
            }
        }

        /// <summary>
        /// Ensures the parent directory of a path exists.
        /// </summary>
        private Result<Unit, FileSystemError> EnsureParentDirectoryExists(string systemPath)
        {
            try
            {
                var parentDir = Path.GetDirectoryName(systemPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }
                return Result<Unit, FileSystemError>.Success(Unit.Value);
            }
            catch (Exception ex)
            {
                return Result<Unit, FileSystemError>.Failure(
                    FileSystemError.FromException(ex, systemPath));
            }
        }

        /// <summary>
        /// Validates that source and destination paths are different.
        /// </summary>
        private Result<Unit, FileSystemError> ValidateSourceDestinationDifferent(CanonicalPath source, CanonicalPath destination)
        {
            if (source.Value.Equals(destination.Value, StringComparison.OrdinalIgnoreCase))
            {
                return Result<Unit, FileSystemError>.Failure(
                    new FileSystemError(
                        "Source and destination paths are the same",
                        source.Value,
                        FileSystemErrorType.InvalidPath));
            }
            return Result<Unit, FileSystemError>.Success(Unit.Value);
        }

        #region Directory Operation Helpers

        /// <summary>
        /// Checks if two directories are the same or if one is a subdirectory of the other.
        /// </summary>
        private bool IsSubdirectoryOf(string parentPath, string childPath)
        {
            var parent = new System.IO.DirectoryInfo(parentPath).FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var child = new System.IO.DirectoryInfo(childPath).FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            
            return child.StartsWith(parent + Path.DirectorySeparatorChar.ToString(), StringComparison.OrdinalIgnoreCase) ||
                   child.Equals(parent, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Copies a directory and all its contents recursively.
        /// </summary>
        private async Task<Result<Unit, FileSystemError>> CopyDirectoryRecursiveAsync(
            string sourceDirPath, 
            string destDirPath,
            CancellationToken cancellationToken)
        {
            try
            {
                // Create the destination directory
                Directory.CreateDirectory(destDirPath);
                
                // Copy all files in the directory
                var sourceDir = new System.IO.DirectoryInfo(sourceDirPath);
                foreach (var file in sourceDir.GetFiles())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var destFilePath = Path.Combine(destDirPath, file.Name);
                    file.CopyTo(destFilePath, overwrite: false);
                    
                    // Preserve attributes
                    PreserveFileAttributes(file.FullName, destFilePath, preserveTimestamps: true, preserveAttributes: true);
                }
                
                // Copy all subdirectories recursively
                foreach (var subDir in sourceDir.GetDirectories())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var destSubDirPath = Path.Combine(destDirPath, subDir.Name);
                    var result = await CopyDirectoryRecursiveAsync(subDir.FullName, destSubDirPath, cancellationToken);
                    if (result.IsFailure)
                        return result;
                }
                
                // Preserve directory attributes
                try
                {
                    var sourceDirInfo = new System.IO.DirectoryInfo(sourceDirPath);
                    var destDirInfo = new System.IO.DirectoryInfo(destDirPath);
                    destDirInfo.Attributes = sourceDirInfo.Attributes;
                    destDirInfo.CreationTimeUtc = sourceDirInfo.CreationTimeUtc;
                    destDirInfo.LastWriteTimeUtc = sourceDirInfo.LastWriteTimeUtc;
                }
                catch
                {
                    // Best effort
                }
                
                return Result<Unit, FileSystemError>.Success(Unit.Value);
            }
            catch (Exception ex)
            {
                return Result<Unit, FileSystemError>.Failure(
                    FileSystemError.FromException(ex, sourceDirPath));
            }
        }

        #endregion

        #endregion
    }

    /// <summary>
    /// File reader implementation for local file system.
    /// </summary>
    internal sealed class LocalFileReader : IFileReader
    {
        private readonly LocalFileSystem _fileSystem;
        private readonly CanonicalPath _filePath;

        public LocalFileReader(LocalFileSystem fileSystem, CanonicalPath filePath)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _filePath = filePath;
        }

        public async Task<Result<byte[], FileSystemError>> ReadBytesAsync(int maxBytes, CancellationToken cancellationToken = default)
        {
            try
            {
                var openResult = await _fileSystem.OpenReadAsync(_filePath, cancellationToken);
                if (openResult.IsFailure)
                    return Result<byte[], FileSystemError>.Failure(openResult.Error);

                using var stream = openResult.Value;
                var buffer = new byte[Math.Min(maxBytes, (int)stream.Length)];
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                
                if (bytesRead < buffer.Length)
                {
                    Array.Resize(ref buffer, bytesRead);
                }
                
                return Result<byte[], FileSystemError>.Success(buffer);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result<byte[], FileSystemError>.Failure(FileSystemError.FromException(ex, _filePath.Value));
            }
        }
    }

    /// <summary>
    /// Local file system specific sequential traversal strategy.
    /// </summary>
    internal sealed class LocalSequentialTraversalStrategy : SequentialTraversalStrategy
    {
        private readonly LocalFileSystem _fileSystem;

        public LocalSequentialTraversalStrategy(LocalFileSystem fileSystem)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        protected override IFileReader CreateFileReader(IFileEntry fileEntry)
        {
            return new LocalFileReader(_fileSystem, fileEntry.Path);
        }
    }

    /// <summary>
    /// Local file system specific parallel traversal strategy.
    /// </summary>
    internal sealed class LocalParallelTraversalStrategy : ParallelTraversalStrategy
    {
        private readonly LocalFileSystem _fileSystem;

        public LocalParallelTraversalStrategy(LocalFileSystem fileSystem)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        protected override IFileReader CreateFileReader(IFileEntry fileEntry)
        {
            return new LocalFileReader(_fileSystem, fileEntry.Path);
        }
    }
}
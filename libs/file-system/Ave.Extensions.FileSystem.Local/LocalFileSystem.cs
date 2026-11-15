using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.FileSystem.Traversal.Strategies;
using Ave.Extensions.Functional;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ave.Extensions.FileSystem.Local
{
    /// <summary>
    /// Local filesystem implementation of <see cref="IFileSystem"/> for cross-platform file operations.
    /// </summary>
    public class LocalFileSystem : IFileSystem
    {
        private readonly IPathMapper _pathMapper;
        private readonly ILogger<LocalFileSystem> _logger;

        /// <summary>
        /// Initializes a new instance of the LocalFileSystem class.
        /// </summary>
        /// <param name="pathMapper">Path mapper for canonical path support.</param>
        public LocalFileSystem(IPathMapper pathMapper)
            : this(pathMapper, NullLogger<LocalFileSystem>.Instance)
        {
        }

        /// <summary>
        /// Initializes a new instance of the LocalFileSystem class.
        /// </summary>
        /// <param name="pathMapper">Path mapper for canonical path support.</param>
        /// <param name="logger">Logger instance.</param>
        public LocalFileSystem(IPathMapper pathMapper, ILogger<LocalFileSystem> logger)
        {
            _pathMapper = pathMapper ?? throw new ArgumentNullException(nameof(pathMapper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public FileSystemInfo Info { get; } = new FileSystemInfo(
            isDurable: true,           // Local file system persists data
            supportsMonitoring: true,  // Supports FileSystemWatcher
            isReadOnly: false          // Supports read and write operations
        );

        /// <inheritdoc />
        public Task<Result<bool, Error>> FileExistsAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<bool, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value)));

                var systemPath = systemPathResult.Value;
                var exists = File.Exists(systemPath);
                return Task.FromResult(Result<bool, Error>.Success(exists));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<bool, Error>.Failure(
                    FileSystemErrors.FromException(ex, path.Value)));
            }
        }

        /// <inheritdoc />
        public Task<Result<bool, Error>> DirectoryExistsAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<bool, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value)));

                var systemPath = systemPathResult.Value;
                var exists = Directory.Exists(systemPath);
                return Task.FromResult(Result<bool, Error>.Success(exists));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<bool, Error>.Failure(
                    FileSystemErrors.FromException(ex, path.Value)));
            }
        }

        /// <inheritdoc />
        public Task<Result<string, Error>> ReadAllTextAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("ReadAllText: {Path}", path.Value);
            return ExecuteAsync(async () =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Result<string, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value));

                var systemPath = systemPathResult.Value;
                var content = await TextFile.ReadAllTextAsync(systemPath, cancellationToken);
                return Result<string, Error>.Success(content);
            }, path.Value);
        }

        /// <inheritdoc />
        public Task<Result<Stream, Error>> OpenReadAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<Stream, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value)));

                cancellationToken.ThrowIfCancellationRequested();
                var systemPath = systemPathResult.Value;
                var stream = File.OpenRead(systemPath);
                return Task.FromResult(Result<Stream, Error>.Success(stream));
            }, path.Value);
        }

        private Task<Result<FileSystemEntry, Error>> GetEntryInternalAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<FileSystemEntry, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value)));

                cancellationToken.ThrowIfCancellationRequested();
                
                var systemPath = systemPathResult.Value;
                
                // Check if it's a file
                if (File.Exists(systemPath))
                {
                    var fileInfo = new System.IO.FileInfo(systemPath);
                    var entry = fileInfo.ToFileSystemEntry(_pathMapper);
                    return Task.FromResult(Result<FileSystemEntry, Error>.Success(entry));
                }
                
                // Check if it's a directory
                if (Directory.Exists(systemPath))
                {
                    var directoryInfo = new System.IO.DirectoryInfo(systemPath);
                    var entry = directoryInfo.ToFileSystemEntry(_pathMapper);
                    return Task.FromResult(Result<FileSystemEntry, Error>.Success(entry));
                }
                
                return Task.FromResult(Result<FileSystemEntry, Error>.Failure(
                    new Error(FileSystemErrorCodes.NotFound.Path, $"Path not found: {path.Value}").With("path", path.Value)));
            }, path.Value);
        }

        /// <inheritdoc />
        public async Task<Result<TEntry, Error>> GetEntryAsync<TEntry>(
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
                    return Result<TEntry, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value));

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
                
                return Result<TEntry, Error>.Failure(
                    new Error(FileSystemErrorCodes.NotFound.Path, $"Path not found: {path.Value}").With("path", path.Value));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result<TEntry, Error>.Failure(
                    FileSystemErrors.FromException(ex, path.Value));
            }
        }

        /// <inheritdoc />
        public Task<Result<IReadOnlyList<CanonicalPath>, Error>> GetFilesAsync(CanonicalPath path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<IReadOnlyList<CanonicalPath>, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value)));

                cancellationToken.ThrowIfCancellationRequested();
                
                var systemPath = systemPathResult.Value;
                var files = Directory.GetFiles(systemPath, searchPattern, searchOption);
                var mappedFilesResult = MapPathsToCanonical(files);
                if (mappedFilesResult.IsFailure)
                    return Task.FromResult(Result<IReadOnlyList<CanonicalPath>, Error>.Failure(mappedFilesResult.Error));
                
                return Task.FromResult(Result<IReadOnlyList<CanonicalPath>, Error>.Success(mappedFilesResult.Value));
            }, path.Value);
        }

        /// <inheritdoc />
        public Task<Result<IReadOnlyList<CanonicalPath>, Error>> GetDirectoriesAsync(CanonicalPath path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<IReadOnlyList<CanonicalPath>, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value)));

                cancellationToken.ThrowIfCancellationRequested();
                
                var systemPath = systemPathResult.Value;
                var directories = Directory.GetDirectories(systemPath, searchPattern, searchOption);
                var mappedDirectoriesResult = MapPathsToCanonical(directories);
                if (mappedDirectoriesResult.IsFailure)
                    return Task.FromResult(Result<IReadOnlyList<CanonicalPath>, Error>.Failure(mappedDirectoriesResult.Error));
                
                return Task.FromResult(Result<IReadOnlyList<CanonicalPath>, Error>.Success(mappedDirectoriesResult.Value));
            }, path.Value);
        }


        /// <inheritdoc />
        public Task<Result<Unit, Error>> CreateDirectoryAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("CreateDirectory: {Path}", path.Value);
            return ExecuteAsync(() =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value)));

                cancellationToken.ThrowIfCancellationRequested();
                var systemPath = systemPathResult.Value;
                Directory.CreateDirectory(systemPath);
                return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
            }, path.Value);
        }

        /// <inheritdoc />
        public Task<Result<Unit, Error>> WriteAllTextAsync(CanonicalPath path, string contents, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("WriteAllText: {Path}", path.Value);
            return ExecuteAsync(async () =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value));

                if (contents == null)
                    contents = string.Empty;

                var systemPath = systemPathResult.Value;
                await TextFile.WriteAllTextAsync(systemPath, contents, cancellationToken);
                return Result<Unit, Error>.Success(Unit.Value);
            }, path.Value);
        }

        /// <inheritdoc />
        public Task<Result<Stream, Error>> OpenWriteAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<Stream, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value)));

                cancellationToken.ThrowIfCancellationRequested();
                var systemPath = systemPathResult.Value;
                var stream = File.OpenWrite(systemPath);
                return Task.FromResult(Result<Stream, Error>.Success(stream));
            }, path.Value);
        }

        /// <inheritdoc />
        public Task<Result<Stream, Error>> OpenAsync(CanonicalPath path, FileMode mode, FileAccess access, FileShare share, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<Stream, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value)));

                cancellationToken.ThrowIfCancellationRequested();
                var systemPath = systemPathResult.Value;
                var stream = new FileStream(systemPath, mode, access, share);
                return Task.FromResult(Result<Stream, Error>.Success(stream));
            }, path.Value);
        }

        /// <inheritdoc />
        public Task<Result<Unit, Error>> DeleteFileAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("DeleteFile: {Path}", path.Value);
            return ExecuteAsync(() =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value)));

                cancellationToken.ThrowIfCancellationRequested();
                var systemPath = systemPathResult.Value;
                File.Delete(systemPath);
                return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
            }, path.Value);
        }

        /// <inheritdoc />
        public Task<Result<Unit, Error>> DeleteDirectoryAsync(CanonicalPath path, bool recursive = false, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("DeleteDirectory: {Path} (recursive={Recursive})", path.Value, recursive);
            return ExecuteAsync(() =>
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value)));

                cancellationToken.ThrowIfCancellationRequested();
                var systemPath = systemPathResult.Value;
                Directory.Delete(systemPath, recursive);
                return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
            }, path.Value);
        }

        /// <inheritdoc />
        public async Task<Result<TraversalSummary, Error>> TraverseAsync<TEntry>(
            CanonicalPath path,
            ITraversalHandler<TEntry> handler,
            IFileSystemEntryMapper<TEntry> mapper,
            TraversalOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteAsync(async () =>
            {
                options ??= new TraversalOptions();

                var context = TraversalContext.CreateInitial(path);
                var stats = new TraversalStats();
                var completionNotified = false;

                try
                {
                    // Notify handler of traversal start
                    var startResult = await handler.OnTraversalStartAsync(path, cancellationToken);
                    if (startResult.IsFailure)
                        return Result<TraversalSummary, Error>.Failure(startResult.Error);

                    // Get the starting entry as IFileSystemEntry
                    var entryResult = await GetStartingEntryAsync(path, cancellationToken);
                    if (entryResult.IsFailure)
                        return Result<TraversalSummary, Error>.Failure(entryResult.Error);

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
                        return Result<TraversalSummary, Error>.Failure(traversalResult.Error);

                    var summary = CreateSummary(context, stats, cancellationToken);

                    // Notify handler of completion
                    completionNotified = true;
                    var completeResult = await handler.OnTraversalCompleteAsync(summary, cancellationToken);
                    if (completeResult.IsFailure)
                        return Result<TraversalSummary, Error>.Failure(completeResult.Error);

                    return Result<TraversalSummary, Error>.Success(summary);
                }
                finally
                {
                    // Completion is a lifecycle contract, not a success callback. Streaming handlers
                    // hold their consumer open until it fires, so every exit - a failed start, a
                    // missing starting entry, a failed traversal, a thrown exception, cancellation -
                    // has to run it or the consumer waits forever. CancellationToken.None because a
                    // cancelled traversal is exactly the case that must still release the consumer.
                    if (!completionNotified)
                    {
                        completionNotified = true;
                        await handler.OnTraversalCompleteAsync(
                            CreateSummary(context, stats, cancellationToken),
                            CancellationToken.None);
                    }
                }
            }, path.Value);
        }

        private static TraversalSummary CreateSummary(
            TraversalContext context,
            TraversalStats stats,
            CancellationToken cancellationToken) =>
            new TraversalSummary(
                stats.FilesProcessed,
                stats.DirectoriesProcessed,
                stats.TotalBytesProcessed,
                DateTime.UtcNow - context.StartTimeUtc,
                cancellationToken.IsCancellationRequested);

        /// <summary>
        /// Creates a traversal strategy based on the provided hint.
        /// </summary>
        /// <param name="hint">The strategy hint.</param>
        /// <returns>An appropriate traversal strategy.</returns>
        private ITraversalStrategy CreateStrategy(TraversalStrategyHint hint) =>
            hint switch
            {
                TraversalStrategyHint.Parallel => new ParallelTraversalStrategy(),
                _ => new SequentialTraversalStrategy()
            };

        private ITraversalStrategy CreateLocalStrategy(TraversalStrategyHint hint) =>
            hint switch
            {
                TraversalStrategyHint.Parallel => new LocalParallelTraversalStrategy(this),
                _ => new LocalSequentialTraversalStrategy(this)
            };

        /// <summary>
        /// Gets a single entry as IFileSystemEntry for traversal starting point.
        /// </summary>
        /// <param name="path">The path to get the entry for.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the entry or an error.</returns>
        private Task<Result<IFileSystemEntry, Error>> GetStartingEntryAsync(
            CanonicalPath path, 
            CancellationToken cancellationToken)
        {
            try
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<IFileSystemEntry, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value)));

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
                    return Task.FromResult(Result<IFileSystemEntry, Error>.Success(entry));
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
                    return Task.FromResult(Result<IFileSystemEntry, Error>.Success(entry));
                }
                
                return Task.FromResult(Result<IFileSystemEntry, Error>.Failure(
                    new Error(FileSystemErrorCodes.NotFound.Path, $"Path not found: {path.Value}").With("path", path.Value)));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<IFileSystemEntry, Error>.Failure(
                    FileSystemErrors.FromException(ex, path.Value)));
            }
        }

        /// <summary>
        /// Gets the children of a directory for traversal.
        /// </summary>
        /// <param name="path">The directory path.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the children or an error.</returns>
        private Task<Result<IEnumerable<IFileSystemEntry>, Error>> GetChildrenAsync(
            CanonicalPath path, 
            CancellationToken cancellationToken)
        {
            try
            {
                var systemPathResult = GetSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Task.FromResult(Result<IEnumerable<IFileSystemEntry>, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value)));

                var systemPath = systemPathResult.Value;
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(systemPath))
                    return Task.FromResult(Result<IEnumerable<IFileSystemEntry>, Error>.Failure(
                        new Error(FileSystemErrorCodes.NotFound.Directory, $"Directory not found: {path.Value}").With("path", path.Value)));

                List<IFileSystemEntry> entries = [];

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

                return Task.FromResult(Result<IEnumerable<IFileSystemEntry>, Error>.Success(entries));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<IEnumerable<IFileSystemEntry>, Error>.Failure(
                    FileSystemErrors.FromException(ex, path.Value)));
            }
        }

        /// <inheritdoc />
        public Task<Result<IReadOnlyList<CanonicalPath>, Error>> ListFilesAsync(
            CanonicalPath directory,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var mapResult = _pathMapper.MapToSystemPath(directory);
                if (mapResult.IsFailure)
                    return Task.FromResult(Result<IReadOnlyList<CanonicalPath>, Error>.Failure(mapResult.Error));

                var systemPath = mapResult.Value;
                if (!Directory.Exists(systemPath))
                {
                    return Task.FromResult(Result<IReadOnlyList<CanonicalPath>, Error>.Failure(
                        new Error(FileSystemErrorCodes.NotFound.Directory, $"Directory not found: {directory.Value}")
                            .With("path", directory.Value)));
                }

                var files = Directory.GetFiles(systemPath);
                var result = new List<CanonicalPath>(files.Length);

                foreach (var file in files)
                {
                    var canonicalResult = _pathMapper.MapToCanonicalPath(file);
                    if (canonicalResult.IsFailure)
                        return Task.FromResult(Result<IReadOnlyList<CanonicalPath>, Error>.Failure(canonicalResult.Error));
                    result.Add(canonicalResult.Value);
                }

                return Task.FromResult(Result<IReadOnlyList<CanonicalPath>, Error>.Success((IReadOnlyList<CanonicalPath>)result));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Task.FromResult(Result<IReadOnlyList<CanonicalPath>, Error>.Failure(
                    FileSystemErrors.FromException(ex, directory.Value)));
            }
        }

        /// <inheritdoc />
        public Task<Result<IMonitoringSession, Error>> CreateMonitoringSessionAsync(MonitoringOptions options, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (options == null)
                {
                    return Task.FromResult(Result<IMonitoringSession, Error>.Failure(
                        new Error(FileSystemErrorCodes.Unknown, "Monitoring options cannot be null")));
                }


                // Convert canonical path to system path for monitoring
                var systemPathResult = GetSystemPath(options.Path);
                if (systemPathResult.IsFailure)
                {
                    return Task.FromResult(Result<IMonitoringSession, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", options.Path.Value)));
                }

                var systemPath = systemPathResult.Value;
                if (!Directory.Exists(systemPath))
                {
                    return Task.FromResult(Result<IMonitoringSession, Error>.Failure(
                        new Error(FileSystemErrorCodes.NotFound.Directory, $"Directory not found: {options.Path.Value}").With("path", options.Path.Value)));
                }

                var session = new MonitoringSession(options, _pathMapper, _logger);
                _logger.LogInformation("Monitoring session started: {Path} (includeSubdirs={IncludeSubdirs})", options.Path.Value, options.IncludeSubdirectories);
                return Task.FromResult(Result<IMonitoringSession, Error>.Success(session));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<IMonitoringSession, Error>.Failure(
                    FileSystemErrors.FromException(ex, options?.Path.Value)));
            }
        }

        private async Task<Result<T, Error>> ExecuteAsync<T>(Func<Task<Result<T, Error>>> operation, string? path = null)
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
                _logger.LogError(ex, "File system operation failed: {Path}", path ?? "unknown");
                return Result<T, Error>.Failure(FileSystemErrors.FromException(ex, path));
            }
        }


        /// <summary>
        /// Gets the system path from a canonical path using the path mapper.
        /// </summary>
        /// <param name="canonicalPath">The canonical path to map.</param>
        /// <returns>A result containing the system path or an error.</returns>
        private Result<string, Error> GetSystemPath(CanonicalPath canonicalPath)
        {
            return _pathMapper.MapToSystemPath(canonicalPath);
        }
        
        /// <summary>
        /// Maps an array of system paths back to canonical format.
        /// </summary>
        /// <param name="systemPaths">Array of system paths.</param>
        /// <returns>Result containing list of canonical paths or an error.</returns>
        private Result<IReadOnlyList<CanonicalPath>, Error> MapPathsToCanonical(string[] systemPaths)
        {
            List<CanonicalPath> canonicalPaths = [];

            foreach (var systemPath in systemPaths)
            {
                var result = MapPathToCanonical(systemPath);
                if (result.IsSuccess)
                {
                    canonicalPaths.Add(result.Value);
                }
                else
                {
                    // Path mapping failure is an error - don't skip silently
                    return Result<IReadOnlyList<CanonicalPath>, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, $"Failed to map system path to canonical: {result.Error.Message}").With("path", systemPath));
                }
            }
            
            return Result<IReadOnlyList<CanonicalPath>, Error>.Success(canonicalPaths);
        }
        
        /// <summary>
        /// Maps a system path back to canonical format using the path mapper.
        /// </summary>
        /// <param name="systemPath">The system path to map.</param>
        /// <returns>Result containing the canonical path or an error.</returns>
        private Result<CanonicalPath, Error> MapPathToCanonical(string systemPath)
        {
            return _pathMapper.MapToCanonicalPath(systemPath);
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
        public Task<Result<Unit, Error>> MoveFileAsync(
            CanonicalPath source,
            CanonicalPath destination,
            bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("MoveFile: {Source} -> {Destination} (overwrite={Overwrite})", source.Value, destination.Value, overwrite);
            try
            {
                // Validate paths are different
                var validationResult = ValidateSourceDestinationDifferent(source, destination);
                if (validationResult.IsFailure)
                    return Task.FromResult(validationResult);

                // Get system paths
                var sourcePathResult = GetSystemPath(source);
                if (sourcePathResult.IsFailure)
                    return Task.FromResult(Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, sourcePathResult.Error.Message).With("path", source.Value)));
                        
                var destPathResult = GetSystemPath(destination);
                if (destPathResult.IsFailure)
                    return Task.FromResult(Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, destPathResult.Error.Message).With("path", destination.Value)));
                
                var sourcePath = sourcePathResult.Value;
                var destPath = destPathResult.Value;
                
                cancellationToken.ThrowIfCancellationRequested();
                
                // Check if source exists
                if (!File.Exists(sourcePath))
                    return Task.FromResult(Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.NotFound.File, "Source file not found").With("path", source.Value)));
                
                // Check if destination exists and overwrite is false
                if (!overwrite && File.Exists(destPath))
                    return Task.FromResult(Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.FileAlreadyExists, "Destination file already exists").With("path", destination.Value)));
                
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
                
                return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (UnauthorizedAccessException ex)
            {
                return Task.FromResult(Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.IO.AccessDenied, ex.Message).With("path", source.Value)));
            }
            catch (DirectoryNotFoundException ex)
            {
                return Task.FromResult(Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.NotFound.Directory, ex.Message).With("path", source.Value)));
            }
            catch (IOException ex) when (ex.HResult == -2147024891) // ERROR_ACCESS_DENIED on Windows
            {
                return Task.FromResult(Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.IO.AccessDenied, ex.Message).With("path", source.Value)));
            }
            catch (IOException ex)
            {
                // Check if this is a cross-volume move error
                if (IsCrossDeviceMoveException(ex))
                {
                    return Task.FromResult(Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.CrossVolumeMove, "Cannot move file across volumes").With("path", source.Value)));
                }

                return Task.FromResult(Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.IO.General, ex.Message).With("path", source.Value)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<Unit, Error>.Failure(
                    FileSystemErrors.FromException(ex, source.Value)));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Unit, Error>> MoveDirectoryAsync(
            CanonicalPath source,
            CanonicalPath destination,
            CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("MoveDirectory: {Source} -> {Destination}", source.Value, destination.Value);
            try
            {
                // Validate paths are different
                var validationResult = ValidateSourceDestinationDifferent(source, destination);
                if (validationResult.IsFailure)
                    return validationResult;

                // Get system paths
                var sourcePathResult = GetSystemPath(source);
                if (sourcePathResult.IsFailure)
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, sourcePathResult.Error.Message).With("path", source.Value));
                        
                var destPathResult = GetSystemPath(destination);
                if (destPathResult.IsFailure)
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, destPathResult.Error.Message).With("path", destination.Value));
                
                var sourcePath = sourcePathResult.Value;
                var destPath = destPathResult.Value;
                
                cancellationToken.ThrowIfCancellationRequested();
                
                // Check if source exists
                if (!Directory.Exists(sourcePath))
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.NotFound.Directory, "Source directory not found").With("path", source.Value));
                
                // Check if destination already exists
                if (Directory.Exists(destPath))
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.DirectoryAlreadyExists, "Destination directory already exists").With("path", destination.Value));
                
                // Check if destination is a subdirectory of source
                if (IsSubdirectoryOf(sourcePath, destPath))
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, "Cannot move directory to a subdirectory of itself").With("path", source.Value));
                
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
                    
                    return Result<Unit, Error>.Success(Unit.Value);
                }
                catch (IOException ex) when (IsCrossDeviceMoveException(ex))
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
                        return Result<Unit, Error>.Success(Unit.Value);
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
                        
                        return Result<Unit, Error>.Failure(
                            FileSystemErrors.FromException(deleteEx, source.Value));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (UnauthorizedAccessException ex)
            {
                return Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.IO.AccessDenied, ex.Message).With("path", source.Value));
            }
            catch (IOException ex)
            {
                return Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.IO.General, ex.Message).With("path", source.Value));
            }
            catch (Exception ex)
            {
                return Result<Unit, Error>.Failure(
                    FileSystemErrors.FromException(ex, source.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Unit, Error>> CopyFileAsync(
            CanonicalPath source,
            CanonicalPath destination,
            bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("CopyFile: {Source} -> {Destination} (overwrite={Overwrite})", source.Value, destination.Value, overwrite);
            try
            {
                // Validate paths are different
                var validationResult = ValidateSourceDestinationDifferent(source, destination);
                if (validationResult.IsFailure)
                    return validationResult;

                // Get system paths
                var sourcePathResult = GetSystemPath(source);
                if (sourcePathResult.IsFailure)
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, sourcePathResult.Error.Message).With("path", source.Value));
                        
                var destPathResult = GetSystemPath(destination);
                if (destPathResult.IsFailure)
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, destPathResult.Error.Message).With("path", destination.Value));
                
                var sourcePath = sourcePathResult.Value;
                var destPath = destPathResult.Value;
                
                cancellationToken.ThrowIfCancellationRequested();
                
                // Check if source exists
                if (!File.Exists(sourcePath))
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.NotFound.File, "Source file not found").With("path", source.Value));
                
                // Check if destination exists and overwrite is false
                if (!overwrite && File.Exists(destPath))
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.FileAlreadyExists, "Destination file already exists").With("path", destination.Value));
                
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
                
                return Result<Unit, Error>.Success(Unit.Value);
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
                return Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.IO.AccessDenied, ex.Message).With("path", source.Value));
            }
            catch (DirectoryNotFoundException ex)
            {
                return Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.NotFound.Directory, ex.Message).With("path", source.Value));
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
                
                return Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.IO.InsufficientSpace, "Insufficient disk space").With("path", destination.Value));
            }
            catch (IOException ex)
            {
                return Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.IO.General, ex.Message).With("path", source.Value));
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
                
                return Result<Unit, Error>.Failure(
                    FileSystemErrors.FromException(ex, source.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Unit, Error>> CopyDirectoryAsync(
            CanonicalPath source,
            CanonicalPath destination,
            CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("CopyDirectory: {Source} -> {Destination}", source.Value, destination.Value);
            try
            {
                // Validate paths are different
                var validationResult = ValidateSourceDestinationDifferent(source, destination);
                if (validationResult.IsFailure)
                    return validationResult;

                // Get system paths
                var sourcePathResult = GetSystemPath(source);
                if (sourcePathResult.IsFailure)
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, sourcePathResult.Error.Message).With("path", source.Value));
                        
                var destPathResult = GetSystemPath(destination);
                if (destPathResult.IsFailure)
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, destPathResult.Error.Message).With("path", destination.Value));
                
                var sourcePath = sourcePathResult.Value;
                var destPath = destPathResult.Value;
                
                cancellationToken.ThrowIfCancellationRequested();
                
                // Check if source exists
                if (!Directory.Exists(sourcePath))
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.NotFound.Directory, "Source directory not found").With("path", source.Value));
                
                // Check if destination already exists
                if (Directory.Exists(destPath))
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.DirectoryAlreadyExists, "Destination directory already exists").With("path", destination.Value));
                
                // Check if destination is a subdirectory of source
                if (IsSubdirectoryOf(sourcePath, destPath))
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, "Cannot copy directory to a subdirectory of itself").With("path", source.Value));
                
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
                return Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.IO.AccessDenied, ex.Message).With("path", source.Value));
            }
            catch (Exception ex)
            {
                return Result<Unit, Error>.Failure(
                    FileSystemErrors.FromException(ex, source.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Unit, Error>> MoveAsync(
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
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, sourcePathResult.Error.Message).With("path", source.Value));
                
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
                            return Result<Unit, Error>.Failure(
                                new Error(FileSystemErrorCodes.IO.DirectoryAlreadyExists, "Cannot overwrite an existing directory").With("path", destination.Value));
                        }
                    }
                    return await MoveDirectoryAsync(source, destination, cancellationToken);
                }
                else
                {
                    // Source doesn't exist
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.NotFound.Path, "Source path not found").With("path", source.Value));
                }
            }
            catch (Exception ex)
            {
                return Result<Unit, Error>.Failure(
                    FileSystemErrors.FromException(ex, source.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Unit, Error>> CopyAsync(
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
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, sourcePathResult.Error.Message).With("path", source.Value));

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
                            return Result<Unit, Error>.Failure(
                                new Error(FileSystemErrorCodes.IO.DirectoryAlreadyExists, "Cannot overwrite an existing directory").With("path", destination.Value));
                        }
                    }
                    return await CopyDirectoryAsync(source, destination, cancellationToken);
                }
                else
                {
                    // Source doesn't exist
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.NotFound.Path, "Source path not found").With("path", source.Value));
                }
            }
            catch (Exception ex)
            {
                return Result<Unit, Error>.Failure(
                    FileSystemErrors.FromException(ex, source.Value));
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
        private Result<Unit, Error> EnsureParentDirectoryExists(string systemPath)
        {
            try
            {
                var parentDir = Path.GetDirectoryName(systemPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }
                return Result<Unit, Error>.Success(Unit.Value);
            }
            catch (Exception ex)
            {
                return Result<Unit, Error>.Failure(
                    FileSystemErrors.FromException(ex, systemPath));
            }
        }

        /// <summary>
        /// Validates that source and destination paths are different.
        /// </summary>
        private Result<Unit, Error> ValidateSourceDestinationDifferent(CanonicalPath source, CanonicalPath destination)
        {
            if (source.Value.Equals(destination.Value, StringComparison.OrdinalIgnoreCase))
            {
                return Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.IO.InvalidPath, "Source and destination paths are the same").With("path", source.Value));
            }
            return Result<Unit, Error>.Success(Unit.Value);
        }

        #region Directory Operation Helpers

        /// <summary>
        /// Determines if an IOException indicates a cross-volume/cross-device move attempt.
        /// </summary>
        /// <param name="ex">The IOException to check.</param>
        /// <returns>True if the exception indicates a cross-device move error.</returns>
        private static bool IsCrossDeviceMoveException(IOException ex)
        {
            // Unix EXDEV (cross-device link) error codes
            // Linux: errno 18 (EXDEV) maps to HResult 0x80131620 or includes "cross-device"
            // macOS: Similar behavior
            // Windows: Different volume errors vary by operation

            // Check HResult for known cross-device error codes
            const int EXDEV_HRESULT = unchecked((int)0x80131620); // Common .NET mapping for EXDEV
            const int ERROR_NOT_SAME_DEVICE = unchecked((int)0x80070011); // Windows: 0x11 = ERROR_NOT_SAME_DEVICE

            if (ex.HResult == EXDEV_HRESULT || ex.HResult == ERROR_NOT_SAME_DEVICE)
                return true;

            // Fallback to message checking for edge cases and localization variations
            var message = ex.Message;
            return message.IndexOf("cross-device", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("different volume", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("not same device", StringComparison.OrdinalIgnoreCase) >= 0;
        }

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
        private async Task<Result<Unit, Error>> CopyDirectoryRecursiveAsync(
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
                
                return Result<Unit, Error>.Success(Unit.Value);
            }
            catch (Exception ex)
            {
                return Result<Unit, Error>.Failure(
                    FileSystemErrors.FromException(ex, sourceDirPath));
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

        public async Task<Result<byte[], Error>> ReadBytesAsync(int maxBytes, CancellationToken cancellationToken = default)
        {
            try
            {
                var openResult = await _fileSystem.OpenReadAsync(_filePath, cancellationToken);
                if (openResult.IsFailure)
                    return Result<byte[], Error>.Failure(openResult.Error);

                using var stream = openResult.Value;
                var buffer = new byte[Math.Min(maxBytes, (int)stream.Length)];
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                
                if (bytesRead < buffer.Length)
                {
                    Array.Resize(ref buffer, bytesRead);
                }
                
                return Result<byte[], Error>.Success(buffer);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result<byte[], Error>.Failure(FileSystemErrors.FromException(ex, _filePath.Value));
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
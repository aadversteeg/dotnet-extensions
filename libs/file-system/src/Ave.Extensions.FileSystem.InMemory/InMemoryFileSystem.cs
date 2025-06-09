using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.FileSystem.Traversal.Strategies;
using Ave.Extensions.Functional;
using Microsoft.Extensions.Logging;

namespace Ave.Extensions.FileSystem.InMemory
{
    /// <summary>
    /// Thread-safe in-memory implementation of IFileSystem for testing and development purposes.
    /// </summary>
    public class InMemoryFileSystem : IFileSystem, IReadOnlyFileSystem
    {
        private readonly ILogger<InMemoryFileSystem> _logger;
        private readonly ConcurrentDictionary<CanonicalPath, FileNode> _files;
        private readonly ConcurrentDictionary<CanonicalPath, DirectoryNode> _directories;
        private readonly ReaderWriterLockSlim _lock;
        private readonly object _monitorLock = new object();
        private readonly Subject<FileSystemChangedEvent> _fileSystemEventsSubject;

        /// <summary>
        /// Initializes a new instance of the InMemoryFileSystem class.
        /// </summary>
        /// <param name="logger">Logger instance. Can be null.</param>
        public InMemoryFileSystem(ILogger<InMemoryFileSystem>? logger = null)
        {
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<InMemoryFileSystem>.Instance;
            _files = new ConcurrentDictionary<CanonicalPath, FileNode>();
            _directories = new ConcurrentDictionary<CanonicalPath, DirectoryNode>();
            _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
            _fileSystemEventsSubject = new Subject<FileSystemChangedEvent>();
            
            // Ensure root directory exists
            _directories.TryAdd(CanonicalPath.Root, new DirectoryNode(CanonicalPath.Root, DateTime.UtcNow));
        }

        /// <inheritdoc />
        public FileSystemInfo Info { get; } = new FileSystemInfo(
            isDurable: false,          // In-memory data is lost when application terminates
            supportsMonitoring: true,  // Supports monitoring via internal event system
            isReadOnly: false          // Supports read and write operations
        );

        /// <summary>
        /// Gets the observable stream of file system events for monitoring purposes.
        /// </summary>
        internal IObservable<FileSystemChangedEvent> FileSystemEvents => _fileSystemEventsSubject;

        /// <inheritdoc />
        public Task<Result<bool, FileSystemError>> FileExistsAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                _lock.EnterReadLock();
                try
                {
                    var exists = _files.ContainsKey(path);
                    return Task.FromResult(Result<bool, FileSystemError>.Success(exists));
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check file existence: {Path}", path);
                return Task.FromResult(Result<bool, FileSystemError>.Failure(
                    new FileSystemError($"Failed to check file existence: {ex.Message}", path.Value)));
            }
        }

        /// <inheritdoc />
        public Task<Result<bool, FileSystemError>> DirectoryExistsAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                _lock.EnterReadLock();
                try
                {
                    var exists = _directories.ContainsKey(path);
                    return Task.FromResult(Result<bool, FileSystemError>.Success(exists));
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check directory existence: {Path}", path);
                return Task.FromResult(Result<bool, FileSystemError>.Failure(
                    new FileSystemError($"Failed to check directory existence: {ex.Message}", path.Value)));
            }
        }

        /// <inheritdoc />
        public Task<Result<string, FileSystemError>> ReadAllTextAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                _lock.EnterReadLock();
                try
                {
                    if (!_files.TryGetValue(path, out var fileNode))
                    {
                        return Task.FromResult(Result<string, FileSystemError>.Failure(
                            new FileSystemError($"File not found: {path}", path.Value, FileSystemErrorType.FileNotFound)));
                    }

                    fileNode.LastAccessUtc = DateTime.UtcNow;
                    var content = Encoding.UTF8.GetString(fileNode.Content);
                    return Task.FromResult(Result<string, FileSystemError>.Success(content));
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read file: {Path}", path);
                return Task.FromResult(Result<string, FileSystemError>.Failure(
                    new FileSystemError($"Failed to read file: {ex.Message}", path.Value)));
            }
        }

        /// <inheritdoc />
        public Task<Result<Stream, FileSystemError>> OpenReadAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                _lock.EnterReadLock();
                try
                {
                    if (!_files.TryGetValue(path, out var fileNode))
                    {
                        return Task.FromResult(Result<Stream, FileSystemError>.Failure(
                            new FileSystemError($"File not found: {path}", path.Value, FileSystemErrorType.FileNotFound)));
                    }

                    fileNode.LastAccessUtc = DateTime.UtcNow;
                    var stream = new MemoryStream(fileNode.Content, false);
                    return Task.FromResult(Result<Stream, FileSystemError>.Success((Stream)stream));
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open file for reading: {Path}", path);
                return Task.FromResult(Result<Stream, FileSystemError>.Failure(
                    new FileSystemError($"Failed to open file for reading: {ex.Message}", path.Value)));
            }
        }

        private Task<Result<FileSystemEntry, FileSystemError>> GetEntryInternalAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                _lock.EnterReadLock();
                try
                {
                    // Check if it's a file
                    if (_files.TryGetValue(path, out var fileNode))
                    {
                        var entry = new FileSystemEntry(
                            path: path,
                            isDirectory: false,
                            size: fileNode.Content.Length,
                            extension: System.IO.Path.GetExtension(path.GetName()),
                            createdUtc: fileNode.CreatedUtc,
                            modifiedUtc: fileNode.ModifiedUtc,
                            lastAccessUtc: fileNode.LastAccessUtc,
                            attributes: fileNode.Attributes);

                        return Task.FromResult(Result<FileSystemEntry, FileSystemError>.Success(entry));
                    }
                    
                    // Check if it's a directory
                    if (_directories.TryGetValue(path, out var dirNode))
                    {
                        var entry = new FileSystemEntry(
                            path: path,
                            isDirectory: true,
                            size: null,
                            extension: null,
                            createdUtc: dirNode.CreatedUtc,
                            modifiedUtc: dirNode.ModifiedUtc,
                            lastAccessUtc: dirNode.LastAccessUtc,
                            attributes: dirNode.Attributes);

                        return Task.FromResult(Result<FileSystemEntry, FileSystemError>.Success(entry));
                    }

                    return Task.FromResult(Result<FileSystemEntry, FileSystemError>.Failure(
                        new FileSystemError($"Path not found: {path}", path.Value, FileSystemErrorType.PathNotFound)));
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get entry: {Path}", path);
                return Task.FromResult(Result<FileSystemEntry, FileSystemError>.Failure(
                    new FileSystemError($"Failed to get entry: {ex.Message}", path.Value)));
            }
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
                _lock.EnterReadLock();
                try
                {
                    // Check if it's a file
                    if (_files.TryGetValue(path, out var fileNode))
                    {
                        var entryInfo = new Models.EntryInfo(
                            path.GetName(),
                            fileNode.CreatedUtc,
                            fileNode.ModifiedUtc,
                            fileNode.LastAccessUtc,
                            fileNode.Attributes);
                        
                        var fileInfoModel = new Models.FileInfo(
                            entryInfo, 
                            fileNode.Content.Length, 
                            System.IO.Path.GetExtension(path.GetName()));
                        
                        var fileEntry = new Models.FileEntry(path, fileInfoModel);
                        
                        // Create file reader for this specific file
                        var fileReader = new InMemoryFileReader(this, path);
                        return await mapper.MapFileAsync(fileEntry, fileReader, cancellationToken);
                    }
                    
                    // Check if it's a directory
                    if (_directories.TryGetValue(path, out var dirNode))
                    {
                        var entryInfo = new Models.EntryInfo(
                            path.GetName(),
                            dirNode.CreatedUtc,
                            dirNode.ModifiedUtc,
                            dirNode.LastAccessUtc,
                            dirNode.Attributes);
                        
                        var directoryInfoModel = new Models.DirectoryInfo(entryInfo);
                        var directoryEntry = new Models.DirectoryEntry(path, directoryInfoModel);
                        return await mapper.MapDirectoryAsync(directoryEntry, cancellationToken);
                    }
                    
                    return Result<TEntry, FileSystemError>.Failure(
                        new FileSystemError($"Path not found: {path.Value}", path.Value, FileSystemErrorType.PathNotFound));
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result<TEntry, FileSystemError>.Failure(
                    new FileSystemError(
                        $"Failed to get entry: {ex.Message}", 
                        path.Value, 
                        FileSystemErrorType.Unknown));
            }
        }

        /// <inheritdoc />
        public Task<Result<IReadOnlyList<CanonicalPath>, FileSystemError>> GetFilesAsync(CanonicalPath path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly, CancellationToken cancellationToken = default)
        {
            try
            {
                _lock.EnterReadLock();
                try
                {
                    if (!_directories.ContainsKey(path))
                    {
                        return Task.FromResult(Result<IReadOnlyList<CanonicalPath>, FileSystemError>.Failure(
                            new FileSystemError($"Directory not found: {path}", path.Value, FileSystemErrorType.DirectoryNotFound)));
                    }

                    var files = new List<CanonicalPath>();
                    CollectFiles(path, searchPattern, searchOption, files);
                    
                    return Task.FromResult(Result<IReadOnlyList<CanonicalPath>, FileSystemError>.Success((IReadOnlyList<CanonicalPath>)files));
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get files: {Path}", path);
                return Task.FromResult(Result<IReadOnlyList<CanonicalPath>, FileSystemError>.Failure(
                    new FileSystemError($"Failed to get files: {ex.Message}", path.Value)));
            }
        }

        /// <inheritdoc />
        public Task<Result<IReadOnlyList<CanonicalPath>, FileSystemError>> GetDirectoriesAsync(CanonicalPath path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly, CancellationToken cancellationToken = default)
        {
            try
            {
                _lock.EnterReadLock();
                try
                {
                    if (!_directories.ContainsKey(path))
                    {
                        return Task.FromResult(Result<IReadOnlyList<CanonicalPath>, FileSystemError>.Failure(
                            new FileSystemError($"Directory not found: {path}", path.Value, FileSystemErrorType.DirectoryNotFound)));
                    }

                    var directories = new List<CanonicalPath>();
                    CollectDirectories(path, searchPattern, searchOption, directories);
                    
                    return Task.FromResult(Result<IReadOnlyList<CanonicalPath>, FileSystemError>.Success((IReadOnlyList<CanonicalPath>)directories));
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get directories: {Path}", path);
                return Task.FromResult(Result<IReadOnlyList<CanonicalPath>, FileSystemError>.Failure(
                    new FileSystemError($"Failed to get directories: {ex.Message}", path.Value)));
            }
        }

        /// <inheritdoc />
        public async Task<Result<TraversalSummary, FileSystemError>> TraverseAsync<TEntry>(
            CanonicalPath path,
            ITraversalHandler<TEntry> handler,
            IFileSystemEntryMapper<TEntry> mapper,
            TraversalOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            try
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

                // Select strategy based on hint
                var strategy = CreateInMemoryStrategy(options.StrategyHint);
                
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
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result<TraversalSummary, FileSystemError>.Failure(
                    FileSystemError.FromException(ex, path.Value));
            }
        }

        /// <summary>
        /// Creates a traversal strategy based on the provided hint.
        /// </summary>
        /// <param name="hint">The strategy hint.</param>
        /// <returns>An appropriate traversal strategy.</returns>
        private ITraversalStrategy CreateInMemoryStrategy(TraversalStrategyHint hint)
        {
            return hint switch
            {
                TraversalStrategyHint.Parallel => new InMemoryParallelTraversalStrategy(this),
                TraversalStrategyHint.MemoryEfficient => new InMemorySequentialTraversalStrategy(this),
                TraversalStrategyHint.Search => new InMemorySequentialTraversalStrategy(this),
                _ => new InMemorySequentialTraversalStrategy(this)
            };
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
                _lock.EnterReadLock();
                try
                {
                    // Check if it's a file
                    if (_files.TryGetValue(path, out var fileNode))
                    {
                        var entryInfo = new Models.EntryInfo(
                            path.GetName(),
                            fileNode.CreatedUtc,
                            fileNode.ModifiedUtc,
                            fileNode.LastAccessUtc,
                            fileNode.Attributes);
                        
                        var fileInfoModel = new Models.FileInfo(
                            entryInfo, 
                            fileNode.Content.Length, 
                            System.IO.Path.GetExtension(path.GetName()));
                        
                        IFileSystemEntry entry = new Models.FileEntry(path, fileInfoModel);
                        return Task.FromResult(Result<IFileSystemEntry, FileSystemError>.Success(entry));
                    }
                    
                    // Check if it's a directory
                    if (_directories.TryGetValue(path, out var dirNode))
                    {
                        var entryInfo = new Models.EntryInfo(
                            path.GetName(),
                            dirNode.CreatedUtc,
                            dirNode.ModifiedUtc,
                            dirNode.LastAccessUtc,
                            dirNode.Attributes);
                        
                        var directoryInfoModel = new Models.DirectoryInfo(entryInfo);
                        IFileSystemEntry entry = new Models.DirectoryEntry(path, directoryInfoModel);
                        return Task.FromResult(Result<IFileSystemEntry, FileSystemError>.Success(entry));
                    }
                    
                    return Task.FromResult(Result<IFileSystemEntry, FileSystemError>.Failure(
                        new FileSystemError($"Path not found: {path.Value}", path.Value, FileSystemErrorType.PathNotFound)));
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<IFileSystemEntry, FileSystemError>.Failure(
                    new FileSystemError($"Failed to get entry: {ex.Message}", path.Value, FileSystemErrorType.Unknown)));
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
                cancellationToken.ThrowIfCancellationRequested();

                _lock.EnterReadLock();
                try
                {
                    if (!_directories.ContainsKey(path))
                    {
                        return Task.FromResult(Result<IEnumerable<IFileSystemEntry>, FileSystemError>.Failure(
                            new FileSystemError($"Directory not found: {path.Value}", path.Value, FileSystemErrorType.DirectoryNotFound)));
                    }

                    var entries = new List<IFileSystemEntry>();

                    // Get all files that are direct children of this directory
                    var childFiles = _files.Where(kvp => IsDirectChildOf(kvp.Key, path))
                                           .Select(kvp => kvp.Value);

                    foreach (var fileNode in childFiles)
                    {
                        var entryInfo = new Models.EntryInfo(
                            fileNode.Path.GetName(),
                            fileNode.CreatedUtc,
                            fileNode.ModifiedUtc,
                            fileNode.LastAccessUtc,
                            fileNode.Attributes);
                        
                        var fileInfoModel = new Models.FileInfo(
                            entryInfo, 
                            fileNode.Content.Length, 
                            System.IO.Path.GetExtension(fileNode.Path.GetName()));
                        
                        entries.Add(new Models.FileEntry(fileNode.Path, fileInfoModel));
                    }

                    // Get all directories that are direct children of this directory
                    var childDirectories = _directories.Where(kvp => IsDirectChildOf(kvp.Key, path))
                                                      .Select(kvp => kvp.Value);

                    foreach (var dirNode in childDirectories)
                    {
                        var entryInfo = new Models.EntryInfo(
                            dirNode.Path.GetName(),
                            dirNode.CreatedUtc,
                            dirNode.ModifiedUtc,
                            dirNode.LastAccessUtc,
                            dirNode.Attributes);
                        
                        var directoryInfoModel = new Models.DirectoryInfo(entryInfo);
                        entries.Add(new Models.DirectoryEntry(dirNode.Path, directoryInfoModel));
                    }

                    return Task.FromResult(Result<IEnumerable<IFileSystemEntry>, FileSystemError>.Success((IEnumerable<IFileSystemEntry>)entries));
                }
                finally
                {
                    _lock.ExitReadLock();
                }
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

                // Check if the directory exists
                _lock.EnterReadLock();
                try
                {
                    if (!_directories.ContainsKey(options.Path))
                    {
                        return Task.FromResult(Result<IMonitoringSession, FileSystemError>.Failure(
                            new FileSystemError($"Directory not found: {options.Path.Value}", options.Path.Value, FileSystemErrorType.DirectoryNotFound)));
                    }
                }
                finally
                {
                    _lock.ExitReadLock();
                }

                var session = new InMemoryMonitoringSession(options, this);
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

        /// <inheritdoc />
        public Task<Result<Unit, FileSystemError>> CreateDirectoryAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                _lock.EnterWriteLock();
                try
                {
                    // Create parent directories if they don't exist
                    CreateParentDirectories(path);
                    
                    if (!_directories.ContainsKey(path))
                    {
                        var now = DateTime.UtcNow;
                        _directories.TryAdd(path, new DirectoryNode(path, now));
                        
                        // Update parent directory modified time
                        var parentResult = path.GetParent();
                        if (parentResult.IsSuccess && _directories.TryGetValue(parentResult.Value, out var parent))
                        {
                            parent.ModifiedUtc = now;
                        }
                        
                        NotifyFileSystemChange(path, FileSystemChangeType.Created);
                    }
                    
                    return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create directory: {Path}", path);
                return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                    new FileSystemError($"Failed to create directory: {ex.Message}", path.Value)));
            }
        }

        /// <inheritdoc />
        public Task<Result<Unit, FileSystemError>> WriteAllTextAsync(CanonicalPath path, string contents, CancellationToken cancellationToken = default)
        {
            try
            {
                var content = Encoding.UTF8.GetBytes(contents ?? string.Empty);
                
                _lock.EnterWriteLock();
                try
                {
                    // Ensure parent directory exists
                    CreateParentDirectories(path);
                    
                    var now = DateTime.UtcNow;
                    var isNewFile = !_files.ContainsKey(path);
                    
                    var fileNode = _files.AddOrUpdate(path,
                        new FileNode(path, content, now),
                        (key, existing) =>
                        {
                            existing.Content = content;
                            existing.ModifiedUtc = now;
                            existing.LastAccessUtc = now;
                            return existing;
                        });
                    
                    // Update parent directory modified time
                    var parentResult = path.GetParent();
                    if (parentResult.IsSuccess && _directories.TryGetValue(parentResult.Value, out var parent))
                    {
                        parent.ModifiedUtc = now;
                    }
                    
                    NotifyFileSystemChange(path, isNewFile ? FileSystemChangeType.Created : FileSystemChangeType.Changed);
                    
                    return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write file: {Path}", path);
                return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                    new FileSystemError($"Failed to write file: {ex.Message}", path.Value)));
            }
        }

        /// <inheritdoc />
        public Task<Result<Stream, FileSystemError>> OpenWriteAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                var stream = new WriteStream(this, path, _logger);
                return Task.FromResult(Result<Stream, FileSystemError>.Success((Stream)stream));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open file for writing: {Path}", path);
                return Task.FromResult(Result<Stream, FileSystemError>.Failure(
                    new FileSystemError($"Failed to open file for writing: {ex.Message}", path.Value)));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Stream, FileSystemError>> OpenAsync(CanonicalPath path, FileMode mode, FileAccess access, FileShare share, CancellationToken cancellationToken = default)
        {
            // Map file mode and access to appropriate operations
            if (access == FileAccess.Read)
            {
                return await OpenReadAsync(path, cancellationToken);
            }
            else if (access == FileAccess.Write || access == FileAccess.ReadWrite)
            {
                return await OpenWriteAsync(path, cancellationToken);
            }
            
            return Result<Stream, FileSystemError>.Failure(
                new FileSystemError($"Unsupported file access mode: {access}", path.Value, FileSystemErrorType.NotSupported));
        }

        /// <inheritdoc />
        public Task<Result<Unit, FileSystemError>> DeleteFileAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                _lock.EnterWriteLock();
                try
                {
                    if (_files.TryRemove(path, out _))
                    {
                        var now = DateTime.UtcNow;
                        
                        // Update parent directory modified time
                        var parentResult = path.GetParent();
                        if (parentResult.IsSuccess && _directories.TryGetValue(parentResult.Value, out var parent))
                        {
                            parent.ModifiedUtc = now;
                        }
                        
                        NotifyFileSystemChange(path, FileSystemChangeType.Deleted);
                    }
                    
                    return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete file: {Path}", path);
                return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                    new FileSystemError($"Failed to delete file: {ex.Message}", path.Value)));
            }
        }

        /// <inheritdoc />
        public Task<Result<Unit, FileSystemError>> DeleteDirectoryAsync(CanonicalPath path, bool recursive = false, CancellationToken cancellationToken = default)
        {
            try
            {
                _lock.EnterWriteLock();
                try
                {
                    if (!_directories.ContainsKey(path))
                    {
                        return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
                    }
                    
                    if (!recursive)
                    {
                        // Check if directory is empty
                        var hasFiles = _files.Keys.Any(f => f.IsChildOf(path));
                        var hasDirectories = _directories.Keys.Any(d => !d.Equals(path) && d.IsChildOf(path));
                        
                        if (hasFiles || hasDirectories)
                        {
                            return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                                new FileSystemError($"Directory is not empty: {path}", path.Value, FileSystemErrorType.IOException)));
                        }
                    }
                    else
                    {
                        // Delete all files in directory recursively
                        var filesToDelete = _files.Keys.Where(f => f.IsChildOf(path)).ToList();
                        foreach (var file in filesToDelete)
                        {
                            _files.TryRemove(file, out _);
                            NotifyFileSystemChange(file, FileSystemChangeType.Deleted);
                        }
                        
                        // Delete all subdirectories recursively
                        var directoriesToDelete = _directories.Keys
                            .Where(d => !d.Equals(path) && d.IsChildOf(path))
                            .OrderByDescending(d => d.Value.Length) // Delete deepest first
                            .ToList();
                        
                        foreach (var dir in directoriesToDelete)
                        {
                            _directories.TryRemove(dir, out _);
                            NotifyFileSystemChange(dir, FileSystemChangeType.Deleted);
                        }
                    }
                    
                    // Delete the directory itself (but not root)
                    if (!path.Equals(CanonicalPath.Root))
                    {
                        _directories.TryRemove(path, out _);
                        
                        var now = DateTime.UtcNow;
                        
                        // Update parent directory modified time
                        var parentResult = path.GetParent();
                        if (parentResult.IsSuccess && _directories.TryGetValue(parentResult.Value, out var parent))
                        {
                            parent.ModifiedUtc = now;
                        }
                        
                        NotifyFileSystemChange(path, FileSystemChangeType.Deleted);
                    }
                    
                    return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete directory: {Path}", path);
                return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                    new FileSystemError($"Failed to delete directory: {ex.Message}", path.Value)));
            }
        }

        // Internal method for write stream
        internal Task<Result<Unit, FileSystemError>> WriteFileContentAsync(CanonicalPath path, byte[] content)
        {
            try
            {
                _lock.EnterWriteLock();
                try
                {
                    // Ensure parent directory exists
                    CreateParentDirectories(path);
                    
                    var now = DateTime.UtcNow;
                    var isNewFile = !_files.ContainsKey(path);
                    
                    var fileNode = _files.AddOrUpdate(path,
                        new FileNode(path, content, now),
                        (key, existing) =>
                        {
                            existing.Content = content;
                            existing.ModifiedUtc = now;
                            existing.LastAccessUtc = now;
                            return existing;
                        });
                    
                    // Update parent directory modified time
                    var parentResult = path.GetParent();
                    if (parentResult.IsSuccess && _directories.TryGetValue(parentResult.Value, out var parent))
                    {
                        parent.ModifiedUtc = now;
                    }
                    
                    NotifyFileSystemChange(path, isNewFile ? FileSystemChangeType.Created : FileSystemChangeType.Changed);
                    
                    return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write file content: {Path}", path);
                return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                    new FileSystemError($"Failed to write file content: {ex.Message}", path.Value)));
            }
        }

        private void NotifyFileSystemChange(CanonicalPath path, FileSystemChangeType changeType)
        {
            lock (_monitorLock)
            {
                // Notify event stream for monitoring sessions
                var eventArgs = new FileSystemChangedEvent(path, changeType);
                _fileSystemEventsSubject.OnNext(eventArgs);
            }
        }


        private void CollectFiles(CanonicalPath directoryPath, string searchPattern, SearchOption searchOption, List<CanonicalPath> files)
        {
            var childFiles = _files.Keys
                .Where(f => IsDirectChildOf(f, directoryPath))
                .Where(f => MatchesPattern(f.GetName(), searchPattern))
                .ToList();
            
            files.AddRange(childFiles);
            
            if (searchOption == SearchOption.AllDirectories)
            {
                var childDirectories = _directories.Keys
                    .Where(d => IsDirectChildOf(d, directoryPath))
                    .ToList();
                
                foreach (var childDir in childDirectories)
                {
                    CollectFiles(childDir, searchPattern, searchOption, files);
                }
            }
        }

        private void CollectDirectories(CanonicalPath directoryPath, string searchPattern, SearchOption searchOption, List<CanonicalPath> directories)
        {
            var childDirectories = _directories.Keys
                .Where(d => IsDirectChildOf(d, directoryPath))
                .Where(d => MatchesPattern(d.GetName(), searchPattern))
                .ToList();
            
            directories.AddRange(childDirectories);
            
            if (searchOption == SearchOption.AllDirectories)
            {
                foreach (var childDir in childDirectories)
                {
                    CollectDirectories(childDir, searchPattern, searchOption, directories);
                }
            }
        }

        private void CreateParentDirectories(CanonicalPath path)
        {
            var parentResult = path.GetParent();
            if (parentResult.IsFailure || _directories.ContainsKey(parentResult.Value))
                return;
            
            CreateParentDirectories(parentResult.Value);
            
            var now = DateTime.UtcNow;
            _directories.TryAdd(parentResult.Value, new DirectoryNode(parentResult.Value, now));
        }



        private static bool IsChildOf(CanonicalPath childPath, CanonicalPath parentPath)
        {
            return childPath.IsChildOf(parentPath);
        }

        private static bool IsDirectChildOf(CanonicalPath childPath, CanonicalPath parentPath)
        {
            if (!childPath.IsChildOf(parentPath))
                return false;
            
            // Check if the child is exactly one level deeper than the parent
            return childPath.Segments.Length == parentPath.Segments.Length + 1;
        }

        private static bool MatchesPattern(string name, string pattern)
        {
            if (pattern == "*") return true;
            
            // Simple wildcard matching
            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            
            return System.Text.RegularExpressions.Regex.IsMatch(name, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Releases all resources used by the InMemoryFileSystem.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc />
        public Task<Result<Unit, FileSystemError>> MoveFileAsync(
            CanonicalPath source, 
            CanonicalPath destination, 
            bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExecuteWithLock(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // Validate paths are different
                if (source.Value.Equals(destination.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Source and destination paths are the same", 
                            source.Value, FileSystemErrorType.InvalidPath));
                }
                
                // Check if source exists
                if (!_files.TryGetValue(source, out var fileNode))
                {
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Source file not found", 
                            source.Value, FileSystemErrorType.FileNotFound));
                }
                
                // Check if destination exists and overwrite is false
                if (!overwrite && _files.ContainsKey(destination))
                {
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Destination file already exists", 
                            destination.Value, FileSystemErrorType.FileAlreadyExists));
                }
                
                // Ensure parent directory exists
                var parentResult = destination.GetParent();
                if (parentResult.IsSuccess && !_directories.ContainsKey(parentResult.Value))
                {
                    CreateDirectoryRecursive(parentResult.Value);
                }
                
                // Move the file (create new node with new path)
                var movedNode = new FileNode(destination, fileNode.Content, fileNode.CreatedUtc)
                {
                    ModifiedUtc = fileNode.ModifiedUtc,
                    LastAccessUtc = DateTime.UtcNow,
                    Attributes = fileNode.Attributes
                };
                
                _files[destination] = movedNode;
                _files.TryRemove(source, out _);
                
                // Update parent directory modified time
                if (parentResult.IsSuccess && _directories.TryGetValue(parentResult.Value, out var parent))
                {
                    parent.ModifiedUtc = DateTime.UtcNow;
                }
                
                return Result<Unit, FileSystemError>.Success(Unit.Value);
            }));
        }

        /// <inheritdoc />
        public Task<Result<Unit, FileSystemError>> MoveDirectoryAsync(
            CanonicalPath source, 
            CanonicalPath destination,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExecuteWithLock(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // Validate paths are different
                if (source.Value.Equals(destination.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Source and destination paths are the same", 
                            source.Value, FileSystemErrorType.InvalidPath));
                }
                
                // Check if source exists
                if (!_directories.ContainsKey(source))
                {
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Source directory not found", 
                            source.Value, FileSystemErrorType.DirectoryNotFound));
                }
                
                // Check if destination already exists
                if (_directories.ContainsKey(destination))
                {
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Destination directory already exists", 
                            destination.Value, FileSystemErrorType.DirectoryAlreadyExists));
                }
                
                // Check if destination is a subdirectory of source
                if (IsSubdirectoryOf(source.Value, destination.Value))
                {
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Cannot move directory to a subdirectory of itself", 
                            source.Value, FileSystemErrorType.InvalidPath));
                }
                
                // Ensure parent directory exists
                var parentResult = destination.GetParent();
                if (parentResult.IsSuccess && !_directories.ContainsKey(parentResult.Value))
                {
                    CreateDirectoryRecursive(parentResult.Value);
                }
                
                // Get all entries to move
                var sourcePrefix = source.Value.TrimEnd('/') + "/";
                var destPrefix = destination.Value.TrimEnd('/') + "/";
                
                // Move directories
                var directoriesToMove = _directories.Keys
                    .Where(d => d.Equals(source) || d.Value.StartsWith(sourcePrefix))
                    .ToList();
                
                foreach (var dir in directoriesToMove)
                {
                    var dirNode = _directories[dir];
                    _directories.TryRemove(dir, out _);
                    
                    var newPath = dir.Equals(source) 
                        ? destination 
                        : CanonicalPath.Create(destPrefix + dir.Value.Substring(sourcePrefix.Length)).Value;
                    
                    var movedDirNode = new DirectoryNode(newPath, dirNode.CreatedUtc)
                    {
                        ModifiedUtc = dirNode.ModifiedUtc,
                        LastAccessUtc = dirNode.LastAccessUtc,
                        Attributes = dirNode.Attributes
                    };
                    _directories[newPath] = movedDirNode;
                }
                
                // Move files
                var filesToMove = _files.Keys
                    .Where(f => f.Value.StartsWith(sourcePrefix))
                    .ToList();
                
                foreach (var file in filesToMove)
                {
                    var fileNode = _files[file];
                    var newPath = CanonicalPath.Create(destPrefix + file.Value.Substring(sourcePrefix.Length)).Value;
                    
                    var movedFileNode = new FileNode(newPath, fileNode.Content, fileNode.CreatedUtc)
                    {
                        ModifiedUtc = fileNode.ModifiedUtc,
                        LastAccessUtc = fileNode.LastAccessUtc,
                        Attributes = fileNode.Attributes
                    };
                    
                    _files[newPath] = movedFileNode;
                    _files.TryRemove(file, out _);
                }
                
                return Result<Unit, FileSystemError>.Success(Unit.Value);
            }));
        }

        /// <inheritdoc />
        public Task<Result<Unit, FileSystemError>> CopyFileAsync(
            CanonicalPath source, 
            CanonicalPath destination, 
            bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExecuteWithLock(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // Validate paths are different
                if (source.Value.Equals(destination.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Source and destination paths are the same", 
                            source.Value, FileSystemErrorType.InvalidPath));
                }
                
                // Check if source exists
                if (!_files.TryGetValue(source, out var sourceNode))
                {
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Source file not found", 
                            source.Value, FileSystemErrorType.FileNotFound));
                }
                
                // Check if destination exists and overwrite is false
                if (!overwrite && _files.ContainsKey(destination))
                {
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Destination file already exists", 
                            destination.Value, FileSystemErrorType.FileAlreadyExists));
                }
                
                // Ensure parent directory exists
                var parentResult = destination.GetParent();
                if (parentResult.IsSuccess && !_directories.ContainsKey(parentResult.Value))
                {
                    CreateDirectoryRecursive(parentResult.Value);
                }
                
                // Deep copy the file data
                var copiedNode = new FileNode(
                    destination,
                    sourceNode.Content.ToArray(), // Deep copy
                    DateTime.UtcNow)
                {
                    Attributes = sourceNode.Attributes
                };
                
                _files[destination] = copiedNode;
                
                // Update source access time
                sourceNode.LastAccessUtc = DateTime.UtcNow;
                
                // Update parent directory modified time
                if (parentResult.IsSuccess && _directories.TryGetValue(parentResult.Value, out var parent))
                {
                    parent.ModifiedUtc = DateTime.UtcNow;
                }
                
                return Result<Unit, FileSystemError>.Success(Unit.Value);
            }));
        }

        /// <inheritdoc />
        public Task<Result<Unit, FileSystemError>> CopyDirectoryAsync(
            CanonicalPath source, 
            CanonicalPath destination,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExecuteWithLock(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // Validate paths are different
                if (source.Value.Equals(destination.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Source and destination paths are the same", 
                            source.Value, FileSystemErrorType.InvalidPath));
                }
                
                // Check if source exists
                if (!_directories.ContainsKey(source))
                {
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Source directory not found", 
                            source.Value, FileSystemErrorType.DirectoryNotFound));
                }
                
                // Check if destination already exists
                if (_directories.ContainsKey(destination))
                {
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Destination directory already exists", 
                            destination.Value, FileSystemErrorType.DirectoryAlreadyExists));
                }
                
                // Check if destination is a subdirectory of source
                if (IsSubdirectoryOf(source.Value, destination.Value))
                {
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Cannot copy directory to a subdirectory of itself", 
                            source.Value, FileSystemErrorType.InvalidPath));
                }
                
                // Ensure parent directory exists
                var parentResult = destination.GetParent();
                if (parentResult.IsSuccess && !_directories.ContainsKey(parentResult.Value))
                {
                    CreateDirectoryRecursive(parentResult.Value);
                }
                
                // Get all entries to copy
                var sourcePrefix = source.Value.TrimEnd('/') + "/";
                var destPrefix = destination.Value.TrimEnd('/') + "/";
                
                // Copy directories
                var directoriesToCopy = _directories
                    .Where(kvp => kvp.Key.Equals(source) || kvp.Key.Value.StartsWith(sourcePrefix))
                    .ToList();
                
                foreach (var kvp in directoriesToCopy)
                {
                    var newPath = kvp.Key.Equals(source) 
                        ? destination 
                        : CanonicalPath.Create(destPrefix + kvp.Key.Value.Substring(sourcePrefix.Length)).Value;
                    
                    var copiedDirNode = new DirectoryNode(newPath, DateTime.UtcNow)
                    {
                        Attributes = kvp.Value.Attributes
                    };
                    _directories[newPath] = copiedDirNode;
                }
                
                // Copy files with deep copy
                var filesToCopy = _files
                    .Where(kvp => kvp.Key.Value.StartsWith(sourcePrefix))
                    .ToList();
                
                foreach (var kvp in filesToCopy)
                {
                    var newPath = CanonicalPath.Create(destPrefix + kvp.Key.Value.Substring(sourcePrefix.Length)).Value;
                    
                    // Deep copy file data
                    var copiedFileNode = new FileNode(
                        newPath,
                        kvp.Value.Content.ToArray(), // Deep copy
                        DateTime.UtcNow)
                    {
                        Attributes = kvp.Value.Attributes
                    };
                    
                    _files[newPath] = copiedFileNode;
                }
                
                return Result<Unit, FileSystemError>.Success(Unit.Value);
            }));
        }

        /// <inheritdoc />
        public async Task<Result<Unit, FileSystemError>> MoveAsync(
            CanonicalPath source, 
            CanonicalPath destination, 
            bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            // Check if source is a file or directory
            var fileExists = await FileExistsAsync(source, cancellationToken);
            if (fileExists.IsSuccess && fileExists.Value)
            {
                return await MoveFileAsync(source, destination, overwrite, cancellationToken);
            }
            
            var dirExists = await DirectoryExistsAsync(source, cancellationToken);
            if (dirExists.IsSuccess && dirExists.Value)
            {
                // For directories, overwrite parameter doesn't apply
                if (overwrite)
                {
                    // Check if destination exists and is a directory
                    var destDirExists = await DirectoryExistsAsync(destination, cancellationToken);
                    if (destDirExists.IsSuccess && destDirExists.Value)
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
            
            return Result<Unit, FileSystemError>.Failure(
                new FileSystemError("Source path not found", source.Value, FileSystemErrorType.PathNotFound));
        }

        /// <inheritdoc />
        public async Task<Result<Unit, FileSystemError>> CopyAsync(
            CanonicalPath source, 
            CanonicalPath destination, 
            bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            // Check if source is a file or directory
            var fileExists = await FileExistsAsync(source, cancellationToken);
            if (fileExists.IsSuccess && fileExists.Value)
            {
                return await CopyFileAsync(source, destination, overwrite, cancellationToken);
            }
            
            var dirExists = await DirectoryExistsAsync(source, cancellationToken);
            if (dirExists.IsSuccess && dirExists.Value)
            {
                // For directories, overwrite parameter doesn't apply
                if (overwrite)
                {
                    // Check if destination exists and is a directory
                    var destDirExists = await DirectoryExistsAsync(destination, cancellationToken);
                    if (destDirExists.IsSuccess && destDirExists.Value)
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
            
            return Result<Unit, FileSystemError>.Failure(
                new FileSystemError("Source path not found", source.Value, FileSystemErrorType.PathNotFound));
        }

        private bool IsSubdirectoryOf(string parentPath, string childPath)
        {
            var parent = parentPath.TrimEnd('/');
            var child = childPath.TrimEnd('/');
            
            return child.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase) ||
                   child.Equals(parent, StringComparison.OrdinalIgnoreCase);
        }

        private void CreateDirectoryRecursive(CanonicalPath path)
        {
            if (_directories.ContainsKey(path))
                return;
            
            var parentResult = path.GetParent();
            if (parentResult.IsSuccess && !_directories.ContainsKey(parentResult.Value))
            {
                CreateDirectoryRecursive(parentResult.Value);
            }
            
            var now = DateTime.UtcNow;
            _directories.TryAdd(path, new DirectoryNode(path, now));
        }

        private Result<T, FileSystemError> ExecuteWithLock<T>(Func<Result<T, FileSystemError>> action)
        {
            _lock.EnterWriteLock();
            try
            {
                return action();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result<T, FileSystemError>.Failure(
                    FileSystemError.FromException(ex));
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Releases the managed and unmanaged resources used by the InMemoryFileSystem.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _lock?.Dispose();
                
                lock (_monitorLock)
                {
                    _fileSystemEventsSubject.OnCompleted();
                    _fileSystemEventsSubject.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Sequential traversal strategy for in-memory file system that can create InMemoryFileReader instances.
    /// </summary>
    internal sealed class InMemorySequentialTraversalStrategy : SequentialTraversalStrategy
    {
        private readonly InMemoryFileSystem _fileSystem;

        public InMemorySequentialTraversalStrategy(InMemoryFileSystem fileSystem)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        protected override IFileReader CreateFileReader(IFileEntry fileEntry)
        {
            return new InMemoryFileReader(_fileSystem, fileEntry.Path);
        }
    }

    /// <summary>
    /// Parallel traversal strategy for in-memory file system that can create InMemoryFileReader instances.
    /// </summary>
    internal sealed class InMemoryParallelTraversalStrategy : ParallelTraversalStrategy
    {
        private readonly InMemoryFileSystem _fileSystem;

        public InMemoryParallelTraversalStrategy(InMemoryFileSystem fileSystem, int? maxConcurrency = null) 
            : base(maxConcurrency)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        protected override IFileReader CreateFileReader(IFileEntry fileEntry)
        {
            return new InMemoryFileReader(_fileSystem, fileEntry.Path);
        }
    }

    /// <summary>
    /// File reader implementation for in-memory file system.
    /// </summary>
    internal sealed class InMemoryFileReader : IFileReader
    {
        private readonly InMemoryFileSystem _fileSystem;
        private readonly CanonicalPath _filePath;

        public InMemoryFileReader(InMemoryFileSystem fileSystem, CanonicalPath filePath)
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
                return Result<byte[], FileSystemError>.Failure(
                    new FileSystemError($"Failed to read file: {ex.Message}", _filePath.Value, FileSystemErrorType.IOException));
            }
        }
    }
}
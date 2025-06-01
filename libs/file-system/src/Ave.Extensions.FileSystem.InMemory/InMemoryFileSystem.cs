using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
        private readonly List<FileSystemMonitor> _monitors;

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
            _monitors = new List<FileSystemMonitor>();
            
            // Ensure root directory exists
            _directories.TryAdd(CanonicalPath.Root, new DirectoryNode(CanonicalPath.Root, DateTime.UtcNow));
        }

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
                    new FileSystemError($"Failed to check file existence: {ex.Message}", path)));
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
                    new FileSystemError($"Failed to check directory existence: {ex.Message}", path)));
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
                            new FileSystemError($"File not found: {path}", path, FileSystemErrorType.FileNotFound)));
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
                    new FileSystemError($"Failed to read file: {ex.Message}", path)));
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
                            new FileSystemError($"File not found: {path}", path, FileSystemErrorType.FileNotFound)));
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
                    new FileSystemError($"Failed to open file for reading: {ex.Message}", path)));
            }
        }

        /// <inheritdoc />
        public Task<Result<FileSystemEntry, FileSystemError>> GetEntryAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                _lock.EnterReadLock();
                try
                {
                    // Check if it's a file
                    if (_files.TryGetValue(path, out var fileNode))
                    {
                        var entry = new FileSystemEntry
                        {
                            Path = path,
                            IsDirectory = false,
                            Size = fileNode.Content.Length,
                            Extension = System.IO.Path.GetExtension(path.GetName()),
                            CreatedUtc = fileNode.CreatedUtc,
                            ModifiedUtc = fileNode.ModifiedUtc,
                            LastAccessUtc = fileNode.LastAccessUtc,
                            Attributes = fileNode.Attributes
                        };

                        return Task.FromResult(Result<FileSystemEntry, FileSystemError>.Success(entry));
                    }
                    
                    // Check if it's a directory
                    if (_directories.TryGetValue(path, out var dirNode))
                    {
                        var entry = new FileSystemEntry
                        {
                            Path = path,
                            IsDirectory = true,
                            Size = null,
                            Extension = null,
                            CreatedUtc = dirNode.CreatedUtc,
                            ModifiedUtc = dirNode.ModifiedUtc,
                            LastAccessUtc = dirNode.LastAccessUtc,
                            Attributes = dirNode.Attributes
                        };

                        return Task.FromResult(Result<FileSystemEntry, FileSystemError>.Success(entry));
                    }

                    return Task.FromResult(Result<FileSystemEntry, FileSystemError>.Failure(
                        new FileSystemError($"Path not found: {path}", path, FileSystemErrorType.PathNotFound)));
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
                    new FileSystemError($"Failed to get entry: {ex.Message}", path)));
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
                            new FileSystemError($"Directory not found: {path}", path, FileSystemErrorType.DirectoryNotFound)));
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
                    new FileSystemError($"Failed to get files: {ex.Message}", path)));
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
                            new FileSystemError($"Directory not found: {path}", path, FileSystemErrorType.DirectoryNotFound)));
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
                    new FileSystemError($"Failed to get directories: {ex.Message}", path)));
            }
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<Result<FileSystemEntry, FileSystemError>> EnumerateFileSystemEntriesAsync(
            CanonicalPath path,
            string searchPattern = "*",
            Models.EnumerationOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            options ??= Models.EnumerationOptions.Default;
            
            await foreach (var entry in EnumerateEntriesRecursive(path, searchPattern, options, 0, cancellationToken))
            {
                yield return entry;
            }
        }

        /// <inheritdoc />
        public Task<IFileSystemMonitor> CreateMonitorAsync(CancellationToken cancellationToken = default)
        {
            lock (_monitorLock)
            {
                var monitor = new FileSystemMonitor(_logger);
                _monitors.Add(monitor);
                return Task.FromResult<IFileSystemMonitor>(monitor);
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
                    new FileSystemError($"Failed to create directory: {ex.Message}", path)));
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
                    new FileSystemError($"Failed to write file: {ex.Message}", path)));
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
                    new FileSystemError($"Failed to open file for writing: {ex.Message}", path)));
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
                new FileSystemError($"Unsupported file access mode: {access}", path, FileSystemErrorType.NotSupported));
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
                    new FileSystemError($"Failed to delete file: {ex.Message}", path)));
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
                                new FileSystemError($"Directory is not empty: {path}", path, FileSystemErrorType.IOException)));
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
                    new FileSystemError($"Failed to delete directory: {ex.Message}", path)));
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
                    new FileSystemError($"Failed to write file content: {ex.Message}", path)));
            }
        }

        private void NotifyFileSystemChange(CanonicalPath path, FileSystemChangeType changeType)
        {
            lock (_monitorLock)
            {
                foreach (var monitor in _monitors.ToList())
                {
                    monitor.NotifyChange(path, changeType);
                }
            }
        }

        private async IAsyncEnumerable<Result<FileSystemEntry, FileSystemError>> EnumerateEntriesRecursive(
            CanonicalPath path,
            string searchPattern,
            Models.EnumerationOptions options,
            int currentDepth,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (options.MaxDepth.HasValue && currentDepth > options.MaxDepth.Value)
                yield break;
            
            _lock.EnterReadLock();
            try
            {
                if (!_directories.ContainsKey(path))
                {
                    yield return Result<FileSystemEntry, FileSystemError>.Failure(
                        new FileSystemError($"Directory not found: {path}", path, FileSystemErrorType.DirectoryNotFound));
                    yield break;
                }

                // Enumerate directories
                var childDirectories = _directories.Keys
                    .Where(d => IsDirectChildOf(d, path))
                    .ToList();

                foreach (var childDir in childDirectories)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var dirName = childDir.GetName();
                    
                    if (!options.IncludeHidden && dirName.StartsWith('.'))
                        continue;
                    
                    if (options.ShouldIncludeDirectory != null && !options.ShouldIncludeDirectory(childDir))
                        continue;
                    
                    if (_directories.TryGetValue(childDir, out var dirNode))
                    {
                        var entry = new FileSystemEntry
                        {
                            Path = childDir,
                            IsDirectory = true,
                            Size = null,
                            Extension = null,
                            CreatedUtc = dirNode.CreatedUtc,
                            ModifiedUtc = dirNode.ModifiedUtc,
                            LastAccessUtc = dirNode.LastAccessUtc,
                            Attributes = dirNode.Attributes
                        };
                        
                        yield return Result<FileSystemEntry, FileSystemError>.Success(entry);
                        
                        // Recurse if enabled
                        if (options.RecurseSubdirectories)
                        {
                            await foreach (var subEntry in EnumerateEntriesRecursive(childDir, searchPattern, options, currentDepth + 1, cancellationToken))
                            {
                                yield return subEntry;
                            }
                        }
                    }
                }

                // Enumerate files
                var childFiles = _files.Keys
                    .Where(f => IsDirectChildOf(f, path))
                    .ToList();

                foreach (var childFile in childFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var fileName = childFile.GetName();
                    
                    if (!MatchesPattern(fileName, searchPattern))
                        continue;
                    
                    if (!options.IncludeHidden && fileName.StartsWith('.'))
                        continue;
                    
                    if (options.ShouldIncludeFile != null && !options.ShouldIncludeFile(childFile))
                        continue;
                    
                    if (_files.TryGetValue(childFile, out var fileNode))
                    {
                        var entry = new FileSystemEntry
                        {
                            Path = childFile,
                            IsDirectory = false,
                            Size = fileNode.Content.Length,
                            Extension = System.IO.Path.GetExtension(childFile.GetName()),
                            CreatedUtc = fileNode.CreatedUtc,
                            ModifiedUtc = fileNode.ModifiedUtc,
                            LastAccessUtc = fileNode.LastAccessUtc,
                            Attributes = fileNode.Attributes
                        };
                        
                        yield return Result<FileSystemEntry, FileSystemError>.Success(entry);
                    }
                }
            }
            finally
            {
                _lock.ExitReadLock();
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
                    foreach (var monitor in _monitors)
                    {
                        monitor.Dispose();
                    }
                    _monitors.Clear();
                }
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Local
{
    /// <summary>
    /// Local filesystem implementation of <see cref="IFileSystem"/> for cross-platform file operations.
    /// </summary>
    public class LocalFileSystem : IFileSystem
    {
        private readonly IPathMapper? _pathMapper;
        
        /// <summary>
        /// Initializes a new instance of the LocalFileSystem class.
        /// </summary>
        /// <param name="pathMapper">Optional path mapper for canonical path support.</param>
        public LocalFileSystem(IPathMapper? pathMapper = null)
        {
            _pathMapper = pathMapper;
        }

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
                    var entry = FileSystemEntry.FromFileInfo(fileInfo, _pathMapper);
                    return Task.FromResult(Result<FileSystemEntry, FileSystemError>.Success(entry));
                }
                
                // Check if it's a directory
                if (Directory.Exists(systemPath))
                {
                    var directoryInfo = new DirectoryInfo(systemPath);
                    var entry = FileSystemEntry.FromDirectoryInfo(directoryInfo, _pathMapper);
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
        public async IAsyncEnumerable<Result<FileSystemEntry, FileSystemError>> EnumerateFileSystemEntriesAsync(
            CanonicalPath path,
            string searchPattern = "*",
            Models.EnumerationOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var systemPathResult = GetSystemPath(path);
            if (systemPathResult.IsFailure)
            {
                yield return Result<FileSystemEntry, FileSystemError>.Failure(
                    new FileSystemError(systemPathResult.Error.Message, path.Value, FileSystemErrorType.InvalidPath));
                yield break;
            }

            options ??= Models.EnumerationOptions.Default;

            var systemPath = systemPathResult.Value;
            await foreach (var entry in EnumerateEntries(systemPath, searchPattern, options, 0, cancellationToken, path))
            {
                yield return entry;
            }
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
        public Task<IFileSystemMonitor> CreateMonitorAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var monitor = new FileSystemMonitor();
            return Task.FromResult<IFileSystemMonitor>(monitor);
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

        private async IAsyncEnumerable<Result<FileSystemEntry, FileSystemError>> EnumerateEntries(
            string path,
            string searchPattern,
            Models.EnumerationOptions options,
            int currentDepth,
            [EnumeratorCancellation] CancellationToken cancellationToken,
            CanonicalPath originalPath)
        {
            if (options.MaxDepth.HasValue && currentDepth > options.MaxDepth.Value)
                yield break;

            DirectoryInfo directoryInfo;
            directoryInfo = new DirectoryInfo(path);
            if (!directoryInfo.Exists)
            {
                yield return Result<FileSystemEntry, FileSystemError>.Failure(
                    new FileSystemError($"Directory not found: {path}", path, FileSystemErrorType.DirectoryNotFound));
                yield break;
            }

            // Enumerate files
            FileInfo[] files;
            files = directoryInfo.GetFiles(searchPattern);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Apply filters
                if (!options.IncludeHidden && file.Attributes.HasFlag(FileAttributes.Hidden))
                    continue;
                if (!options.IncludeSystem && file.Attributes.HasFlag(FileAttributes.System))
                    continue;

                if (options.ShouldIncludeFile != null && !options.ShouldIncludeFile(file.FullName))
                    continue;

                var entry = FileSystemEntry.FromFileInfo(file, _pathMapper);
                yield return Result<FileSystemEntry, FileSystemError>.Success(entry);
            }

            // Enumerate directories
            DirectoryInfo[] directories;
            directories = directoryInfo.GetDirectories();

            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Apply filters
                if (!options.IncludeHidden && directory.Attributes.HasFlag(FileAttributes.Hidden))
                    continue;
                if (!options.IncludeSystem && directory.Attributes.HasFlag(FileAttributes.System))
                    continue;

                if (options.ShouldIncludeDirectory != null && !options.ShouldIncludeDirectory(directory.FullName))
                    continue;

                var entry = FileSystemEntry.FromDirectoryInfo(directory, _pathMapper);
                yield return Result<FileSystemEntry, FileSystemError>.Success(entry);

                // Recurse into subdirectory only if RecurseSubdirectories is enabled
                if (options.RecurseSubdirectories)
                {
                    await foreach (var subEntry in EnumerateEntries(directory.FullName, searchPattern, options, currentDepth + 1, cancellationToken, originalPath))
                    {
                        yield return subEntry;
                    }
                }
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
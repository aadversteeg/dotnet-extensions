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
        public Task<Result<bool, FileSystemError>> FileExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (string.IsNullOrEmpty(path))
                    return Task.FromResult(Result<bool, FileSystemError>.Failure(
                        new FileSystemError("Path cannot be null or empty.", path, FileSystemErrorType.InvalidPath)));

                var systemPath = GetSystemPath(path);
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
                    FileSystemError.FromException(ex, path)));
            }
        }

        /// <inheritdoc />
        public Task<Result<bool, FileSystemError>> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (string.IsNullOrEmpty(path))
                    return Task.FromResult(Result<bool, FileSystemError>.Failure(
                        new FileSystemError("Path cannot be null or empty.", path, FileSystemErrorType.InvalidPath)));

                var systemPath = GetSystemPath(path);
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
                    FileSystemError.FromException(ex, path)));
            }
        }

        /// <inheritdoc />
        public Task<Result<string, FileSystemError>> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(async () =>
            {
                if (string.IsNullOrEmpty(path))
                    return Result<string, FileSystemError>.Failure(
                        new FileSystemError("Path cannot be null or empty.", path, FileSystemErrorType.InvalidPath));

                var systemPath = GetSystemPath(path);
                var content = await File.ReadAllTextAsync(systemPath, cancellationToken);
                return Result<string, FileSystemError>.Success(content);
            }, path);
        }

        /// <inheritdoc />
        public Task<Result<Stream, FileSystemError>> OpenReadAsync(string path, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                if (string.IsNullOrEmpty(path))
                    return Task.FromResult(Result<Stream, FileSystemError>.Failure(
                        new FileSystemError("Path cannot be null or empty.", path, FileSystemErrorType.InvalidPath)));

                cancellationToken.ThrowIfCancellationRequested();
                var systemPath = GetSystemPath(path);
                var stream = File.OpenRead(systemPath);
                return Task.FromResult(Result<Stream, FileSystemError>.Success(stream));
            }, path);
        }

        /// <inheritdoc />
        public Task<Result<FileMetadata, FileSystemError>> GetFileMetadataAsync(string path, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                if (string.IsNullOrEmpty(path))
                    return Task.FromResult(Result<FileMetadata, FileSystemError>.Failure(
                        new FileSystemError("Path cannot be null or empty.", path, FileSystemErrorType.InvalidPath)));

                cancellationToken.ThrowIfCancellationRequested();
                
                var systemPath = GetSystemPath(path);
                var fileInfo = new FileInfo(systemPath);
                if (!fileInfo.Exists)
                    return Task.FromResult(Result<FileMetadata, FileSystemError>.Failure(
                        new FileSystemError($"File not found: {path}", path, FileSystemErrorType.FileNotFound)));

                var metadata = FileMetadata.FromFileInfo(fileInfo);
                // Map the path back to canonical format if needed
                metadata.FullPath = MapPathToCanonical(metadata.FullPath, path);
                return Task.FromResult(Result<FileMetadata, FileSystemError>.Success(metadata));
            }, path);
        }

        /// <inheritdoc />
        public Task<Result<DirectoryMetadata, FileSystemError>> GetDirectoryMetadataAsync(string path, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                if (string.IsNullOrEmpty(path))
                    return Task.FromResult(Result<DirectoryMetadata, FileSystemError>.Failure(
                        new FileSystemError("Path cannot be null or empty.", path, FileSystemErrorType.InvalidPath)));

                cancellationToken.ThrowIfCancellationRequested();
                
                var systemPath = GetSystemPath(path);
                var directoryInfo = new DirectoryInfo(systemPath);
                if (!directoryInfo.Exists)
                    return Task.FromResult(Result<DirectoryMetadata, FileSystemError>.Failure(
                        new FileSystemError($"Directory not found: {path}", path, FileSystemErrorType.DirectoryNotFound)));

                var metadata = DirectoryMetadata.FromDirectoryInfo(directoryInfo);
                // Map the path back to canonical format if needed
                metadata.FullPath = MapPathToCanonical(metadata.FullPath, path);
                return Task.FromResult(Result<DirectoryMetadata, FileSystemError>.Success(metadata));
            }, path);
        }

        /// <inheritdoc />
        public Task<Result<string[], FileSystemError>> GetFilesAsync(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                if (string.IsNullOrEmpty(path))
                    return Task.FromResult(Result<string[], FileSystemError>.Failure(
                        new FileSystemError("Path cannot be null or empty.", path, FileSystemErrorType.InvalidPath)));

                cancellationToken.ThrowIfCancellationRequested();
                
                var systemPath = GetSystemPath(path);
                var files = Directory.GetFiles(systemPath, searchPattern, searchOption);
                var mappedFiles = MapPathsToCanonical(files, path);
                return Task.FromResult(Result<string[], FileSystemError>.Success(mappedFiles));
            }, path);
        }

        /// <inheritdoc />
        public Task<Result<string[], FileSystemError>> GetDirectoriesAsync(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                if (string.IsNullOrEmpty(path))
                    return Task.FromResult(Result<string[], FileSystemError>.Failure(
                        new FileSystemError("Path cannot be null or empty.", path, FileSystemErrorType.InvalidPath)));

                cancellationToken.ThrowIfCancellationRequested();
                
                var systemPath = GetSystemPath(path);
                var directories = Directory.GetDirectories(systemPath, searchPattern, searchOption);
                var mappedDirectories = MapPathsToCanonical(directories, path);
                return Task.FromResult(Result<string[], FileSystemError>.Success(mappedDirectories));
            }, path);
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<Result<FileSystemEntry, FileSystemError>> EnumerateFileSystemEntriesAsync(
            string path,
            string searchPattern = "*",
            Models.EnumerationOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(path))
            {
                yield return Result<FileSystemEntry, FileSystemError>.Failure(
                    new FileSystemError("Path cannot be null or empty.", path, FileSystemErrorType.InvalidPath));
                yield break;
            }

            options ??= Models.EnumerationOptions.Default;

            var systemPath = GetSystemPath(path);
            await foreach (var entry in EnumerateEntries(systemPath, searchPattern, options, 0, cancellationToken, path))
            {
                yield return entry;
            }
        }

        /// <inheritdoc />
        public Task<Result<Unit, FileSystemError>> CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                if (string.IsNullOrEmpty(path))
                    return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Path cannot be null or empty.", path, FileSystemErrorType.InvalidPath)));

                cancellationToken.ThrowIfCancellationRequested();
                var systemPath = GetSystemPath(path);
                Directory.CreateDirectory(systemPath);
                return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
            }, path);
        }

        /// <inheritdoc />
        public Task<Result<Unit, FileSystemError>> WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(async () =>
            {
                if (string.IsNullOrEmpty(path))
                    return Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Path cannot be null or empty.", path, FileSystemErrorType.InvalidPath));

                if (contents == null)
                    contents = string.Empty;

                var systemPath = GetSystemPath(path);
                await File.WriteAllTextAsync(systemPath, contents, cancellationToken);
                return Result<Unit, FileSystemError>.Success(Unit.Value);
            }, path);
        }

        /// <inheritdoc />
        public Task<Result<Stream, FileSystemError>> OpenWriteAsync(string path, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                if (string.IsNullOrEmpty(path))
                    return Task.FromResult(Result<Stream, FileSystemError>.Failure(
                        new FileSystemError("Path cannot be null or empty.", path, FileSystemErrorType.InvalidPath)));

                cancellationToken.ThrowIfCancellationRequested();
                var systemPath = GetSystemPath(path);
                var stream = File.OpenWrite(systemPath);
                return Task.FromResult(Result<Stream, FileSystemError>.Success(stream));
            }, path);
        }

        /// <inheritdoc />
        public Task<Result<Stream, FileSystemError>> OpenAsync(string path, FileMode mode, FileAccess access, FileShare share, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                if (string.IsNullOrEmpty(path))
                    return Task.FromResult(Result<Stream, FileSystemError>.Failure(
                        new FileSystemError("Path cannot be null or empty.", path, FileSystemErrorType.InvalidPath)));

                cancellationToken.ThrowIfCancellationRequested();
                var systemPath = GetSystemPath(path);
                var stream = new FileStream(systemPath, mode, access, share);
                return Task.FromResult(Result<Stream, FileSystemError>.Success(stream));
            }, path);
        }

        /// <inheritdoc />
        public Task<Result<Unit, FileSystemError>> DeleteFileAsync(string path, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                if (string.IsNullOrEmpty(path))
                    return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Path cannot be null or empty.", path, FileSystemErrorType.InvalidPath)));

                cancellationToken.ThrowIfCancellationRequested();
                var systemPath = GetSystemPath(path);
                File.Delete(systemPath);
                return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
            }, path);
        }

        /// <inheritdoc />
        public Task<Result<Unit, FileSystemError>> DeleteDirectoryAsync(string path, bool recursive = false, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() =>
            {
                if (string.IsNullOrEmpty(path))
                    return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                        new FileSystemError("Path cannot be null or empty.", path, FileSystemErrorType.InvalidPath)));

                cancellationToken.ThrowIfCancellationRequested();
                var systemPath = GetSystemPath(path);
                Directory.Delete(systemPath, recursive);
                return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
            }, path);
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
            string? originalPath = null)
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

                var entry = FileSystemEntry.FromFileInfo(file);
                // Map entry path back to canonical format if needed
                if (originalPath != null)
                {
                    entry.Path = MapPathToCanonical(entry.Path, originalPath);
                }
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

                var entry = FileSystemEntry.FromDirectoryInfo(directory);
                // Map entry path back to canonical format if needed
                if (originalPath != null)
                {
                    entry.Path = MapPathToCanonical(entry.Path, originalPath);
                }
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
        /// Gets the system path from an input path, applying canonical mapping if available.
        /// </summary>
        /// <param name="inputPath">The input path that may be in canonical format.</param>
        /// <returns>The system path to use for file operations.</returns>
        private string GetSystemPath(string inputPath)
        {
            if (_pathMapper != null && IsCanonicalPath(inputPath))
            {
                var result = _pathMapper.MapToSystemPath(inputPath);
                if (result.IsSuccess)
                    return result.Value;
            }
            
            // Return original path if no mapping needed or mapping failed
            return inputPath;
        }
        
        /// <summary>
        /// Maps an array of system paths back to canonical format if needed.
        /// </summary>
        /// <param name="systemPaths">Array of system paths.</param>
        /// <param name="originalPath">The original input path to determine if mapping is needed.</param>
        /// <returns>Array of paths in appropriate format.</returns>
        private string[] MapPathsToCanonical(string[] systemPaths, string originalPath)
        {
            if (_pathMapper == null || !IsCanonicalPath(originalPath))
                return systemPaths;
            
            var canonicalPaths = new string[systemPaths.Length];
            for (int i = 0; i < systemPaths.Length; i++)
            {
                var result = _pathMapper.MapToCanonicalPath(systemPaths[i]);
                canonicalPaths[i] = result.IsSuccess ? result.Value : systemPaths[i];
            }
            
            return canonicalPaths;
        }
        
        /// <summary>
        /// Maps a system path back to canonical format if needed.
        /// </summary>
        /// <param name="systemPath">The system path to map.</param>
        /// <param name="originalPath">The original input path to determine if mapping is needed.</param>
        /// <returns>Path in appropriate format.</returns>
        private string MapPathToCanonical(string systemPath, string originalPath)
        {
            if (_pathMapper == null || !IsCanonicalPath(originalPath))
                return systemPath;
            
            var result = _pathMapper.MapToCanonicalPath(systemPath);
            return result.IsSuccess ? result.Value : systemPath;
        }
        
        /// <summary>
        /// Determines if a path is in canonical format.
        /// </summary>
        /// <param name="path">The path to check.</param>
        /// <returns>True if the path is canonical.</returns>
        private static bool IsCanonicalPath(string path)
        {
            // Simple check: canonical paths start with / or are drive letters like /c/
            return path.StartsWith("/") && !path.StartsWith(@"\\");
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
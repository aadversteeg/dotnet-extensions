using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Default implementation of <see cref="IFileSystem"/> for cross-platform file operations.
    /// </summary>
    public class FileSystemService : IFileSystem
    {
        /// <inheritdoc />
        public Task<Result<bool, FileSystemError>> FileExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (string.IsNullOrEmpty(path))
                    return Task.FromResult(Result<bool, FileSystemError>.Failure(
                        new FileSystemError("Path cannot be null or empty.", path, FileSystemErrorType.InvalidPath)));

                var exists = File.Exists(path);
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

                var exists = Directory.Exists(path);
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

                var content = await File.ReadAllTextAsync(path, cancellationToken);
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
                var stream = File.OpenRead(path);
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
                
                var fileInfo = new FileInfo(path);
                if (!fileInfo.Exists)
                    return Task.FromResult(Result<FileMetadata, FileSystemError>.Failure(
                        new FileSystemError($"File not found: {path}", path, FileSystemErrorType.FileNotFound)));

                var metadata = FileMetadata.FromFileInfo(fileInfo);
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
                
                var directoryInfo = new DirectoryInfo(path);
                if (!directoryInfo.Exists)
                    return Task.FromResult(Result<DirectoryMetadata, FileSystemError>.Failure(
                        new FileSystemError($"Directory not found: {path}", path, FileSystemErrorType.DirectoryNotFound)));

                var metadata = DirectoryMetadata.FromDirectoryInfo(directoryInfo);
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
                
                var files = Directory.GetFiles(path, searchPattern, searchOption);
                return Task.FromResult(Result<string[], FileSystemError>.Success(files));
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
                
                var directories = Directory.GetDirectories(path, searchPattern, searchOption);
                return Task.FromResult(Result<string[], FileSystemError>.Success(directories));
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

            await foreach (var entry in EnumerateEntries(path, searchPattern, options, 0, cancellationToken))
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
                Directory.CreateDirectory(path);
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

                await File.WriteAllTextAsync(path, contents, cancellationToken);
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
                var stream = File.OpenWrite(path);
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
                var stream = new FileStream(path, mode, access, share);
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
                File.Delete(path);
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
                Directory.Delete(path, recursive);
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
            [EnumeratorCancellation] CancellationToken cancellationToken)
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
                yield return Result<FileSystemEntry, FileSystemError>.Success(entry);

                // Recurse into subdirectory only if RecurseSubdirectories is enabled
                if (options.RecurseSubdirectories)
                {
                    await foreach (var subEntry in EnumerateEntries(directory.FullName, searchPattern, options, currentDepth + 1, cancellationToken))
                    {
                        yield return subEntry;
                    }
                }
            }
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
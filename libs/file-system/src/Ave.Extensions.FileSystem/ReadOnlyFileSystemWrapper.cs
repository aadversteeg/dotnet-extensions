using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// A wrapper that provides read-only access to a file system.
    /// </summary>
    internal class ReadOnlyFileSystemWrapper : IReadOnlyFileSystem
    {
        private readonly IFileSystem _fileSystem;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReadOnlyFileSystemWrapper"/> class.
        /// </summary>
        /// <param name="fileSystem">The file system to wrap.</param>
        public ReadOnlyFileSystemWrapper(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
        }

        /// <inheritdoc />
        public Task<Result<bool, FileSystemError>> FileExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            return _fileSystem.FileExistsAsync(path, cancellationToken);
        }

        /// <inheritdoc />
        public Task<Result<bool, FileSystemError>> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            return _fileSystem.DirectoryExistsAsync(path, cancellationToken);
        }

        /// <inheritdoc />
        public Task<Result<string, FileSystemError>> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        {
            return _fileSystem.ReadAllTextAsync(path, cancellationToken);
        }

        /// <inheritdoc />
        public Task<Result<Stream, FileSystemError>> OpenReadAsync(string path, CancellationToken cancellationToken = default)
        {
            return _fileSystem.OpenReadAsync(path, cancellationToken);
        }

        /// <inheritdoc />
        public Task<Result<FileMetadata, FileSystemError>> GetFileMetadataAsync(string path, CancellationToken cancellationToken = default)
        {
            return _fileSystem.GetFileMetadataAsync(path, cancellationToken);
        }

        /// <inheritdoc />
        public Task<Result<DirectoryMetadata, FileSystemError>> GetDirectoryMetadataAsync(string path, CancellationToken cancellationToken = default)
        {
            return _fileSystem.GetDirectoryMetadataAsync(path, cancellationToken);
        }

        /// <inheritdoc />
        public Task<Result<string[], FileSystemError>> GetFilesAsync(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly, CancellationToken cancellationToken = default)
        {
            return _fileSystem.GetFilesAsync(path, searchPattern, searchOption, cancellationToken);
        }

        /// <inheritdoc />
        public Task<Result<string[], FileSystemError>> GetDirectoriesAsync(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly, CancellationToken cancellationToken = default)
        {
            return _fileSystem.GetDirectoriesAsync(path, searchPattern, searchOption, cancellationToken);
        }

        /// <inheritdoc />
        public IAsyncEnumerable<Result<FileSystemEntry, FileSystemError>> EnumerateFileSystemEntriesAsync(
            string path,
            string searchPattern = "*",
            Models.EnumerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return _fileSystem.EnumerateFileSystemEntriesAsync(path, searchPattern, options, cancellationToken);
        }
        
        /// <inheritdoc />
        public Task<IFileSystemMonitor> CreateMonitorAsync(CancellationToken cancellationToken = default)
        {
            // Always delegate to the wrapped file system
            return _fileSystem.CreateMonitorAsync(cancellationToken);
        }
    }
}
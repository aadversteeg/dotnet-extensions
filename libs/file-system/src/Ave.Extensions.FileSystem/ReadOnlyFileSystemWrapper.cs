using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
        public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            return _fileSystem.FileExistsAsync(path, cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            return _fileSystem.DirectoryExistsAsync(path, cancellationToken);
        }

        /// <inheritdoc />
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        {
            return _fileSystem.ReadAllTextAsync(path, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IEnumerable<string>> GetFilesAsync(string path, string searchPattern = "*", CancellationToken cancellationToken = default)
        {
            return _fileSystem.GetFilesAsync(path, searchPattern, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IEnumerable<string>> GetDirectoriesAsync(string path, CancellationToken cancellationToken = default)
        {
            return _fileSystem.GetDirectoriesAsync(path, cancellationToken);
        }
        
        /// <inheritdoc />
        public Task<IFileSystemMonitor> CreateMonitorAsync(CancellationToken cancellationToken = default)
        {
            // Always delegate to the wrapped file system
            return _fileSystem.CreateMonitorAsync(cancellationToken);
        }
    }
}
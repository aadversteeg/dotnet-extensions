using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
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
        public FileSystemInfo Info => new FileSystemInfo(
            isDurable: _fileSystem.Info.IsDurable,
            supportsMonitoring: _fileSystem.Info.SupportsMonitoring,
            isReadOnly: true  // This wrapper is always read-only
        );

        /// <inheritdoc />
        public Task<Result<bool, FileSystemError>> FileExistsAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            return _fileSystem.FileExistsAsync(path, cancellationToken);
        }

        /// <inheritdoc />
        public Task<Result<bool, FileSystemError>> DirectoryExistsAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            return _fileSystem.DirectoryExistsAsync(path, cancellationToken);
        }

        /// <inheritdoc />
        public Task<Result<string, FileSystemError>> ReadAllTextAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            return _fileSystem.ReadAllTextAsync(path, cancellationToken);
        }

        /// <inheritdoc />
        public Task<Result<Stream, FileSystemError>> OpenReadAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            return _fileSystem.OpenReadAsync(path, cancellationToken);
        }

        /// <inheritdoc />
        public Task<Result<TEntry, FileSystemError>> GetEntryAsync<TEntry>(
            CanonicalPath path,
            IFileSystemEntryMapper<TEntry> mapper,
            CancellationToken cancellationToken = default)
        {
            return _fileSystem.GetEntryAsync(path, mapper, cancellationToken);
        }

        /// <inheritdoc />
        public Task<Result<TraversalSummary, FileSystemError>> TraverseAsync(
            CanonicalPath path, 
            ITraversalHandler handler, 
            TraversalOptions? options = null, 
            CancellationToken cancellationToken = default)
        {
            return _fileSystem.TraverseAsync(path, handler, options, cancellationToken);
        }

        
        /// <inheritdoc />
        public Task<Result<IMonitoringSession, FileSystemError>> CreateMonitoringSessionAsync(MonitoringOptions options, CancellationToken cancellationToken = default)
        {
            // Always delegate to the wrapped file system
            return _fileSystem.CreateMonitoringSessionAsync(options, cancellationToken);
        }
    }
}
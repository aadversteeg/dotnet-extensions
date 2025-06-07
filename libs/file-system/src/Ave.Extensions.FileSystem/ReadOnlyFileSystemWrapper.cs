using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Abstractions;
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
        public async Task<Result<TEntry, FileSystemError>> GetEntryAsync<TEntry>(
            CanonicalPath path,
            IFileSystemEntryMapper<TEntry> mapper,
            CancellationToken cancellationToken = default)
        {
            // Create a wrapper mapper that provides the IFileReader for files
            var wrapperMapper = new ReadOnlyMapperWrapper<TEntry>(mapper, this);
            return await _fileSystem.GetEntryAsync(path, wrapperMapper, cancellationToken);
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

    /// <summary>
    /// Mapper wrapper that provides IFileReader to the underlying mapper for read-only file system.
    /// </summary>
    /// <typeparam name="TEntry">The target type to map to.</typeparam>
    internal sealed class ReadOnlyMapperWrapper<TEntry> : IFileSystemEntryMapper<TEntry>
    {
        private readonly IFileSystemEntryMapper<TEntry> _mapper;
        private readonly ReadOnlyFileSystemWrapper _fileSystem;

        public ReadOnlyMapperWrapper(IFileSystemEntryMapper<TEntry> mapper, ReadOnlyFileSystemWrapper fileSystem)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public async Task<Result<TEntry, FileSystemError>> MapFileAsync(
            IFileEntry entry,
            IFileReader fileReader,
            CancellationToken cancellationToken = default)
        {
            // Create a read-only file reader for this specific file
            var readOnlyFileReader = new ReadOnlyFileReader(_fileSystem, entry.Path);
            return await _mapper.MapFileAsync(entry, readOnlyFileReader, cancellationToken);
        }

        public Task<Result<TEntry, FileSystemError>> MapDirectoryAsync(
            IDirectoryEntry entry,
            CancellationToken cancellationToken = default)
        {
            return _mapper.MapDirectoryAsync(entry, cancellationToken);
        }
    }

    /// <summary>
    /// File reader implementation for read-only file system wrapper.
    /// </summary>
    internal sealed class ReadOnlyFileReader : IFileReader
    {
        private readonly ReadOnlyFileSystemWrapper _fileSystem;
        private readonly CanonicalPath _filePath;

        public ReadOnlyFileReader(ReadOnlyFileSystemWrapper fileSystem, CanonicalPath filePath)
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
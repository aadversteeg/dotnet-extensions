using Ave.Extensions.ErrorPaths;
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
    /// Provides a read-only cross-platform abstraction for file system operations.
    /// </summary>
    public interface IReadOnlyFileSystem
    {
        /// <summary>
        /// Gets information about the file system capabilities and characteristics.
        /// </summary>
        FileSystemInfo Info { get; }
        /// <summary>
        /// Determines whether the specified file exists.
        /// </summary>
        /// <param name="path">The file path to check.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating whether the file exists or an error occurred.</returns>
        Task<Result<bool, Error>> FileExistsAsync(CanonicalPath path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Determines whether the specified directory exists.
        /// </summary>
        /// <param name="path">The directory path to check.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating whether the directory exists or an error occurred.</returns>
        Task<Result<bool, Error>> DirectoryExistsAsync(CanonicalPath path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads all text from a file.
        /// </summary>
        /// <param name="path">The file path to read from.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the file content or an error.</returns>
        Task<Result<string, Error>> ReadAllTextAsync(CanonicalPath path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a file for reading.
        /// </summary>
        /// <param name="path">The file path to open.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the file stream or an error.</returns>
        Task<Result<Stream, Error>> OpenReadAsync(CanonicalPath path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets metadata for a file system entry and maps it to a custom type.
        /// </summary>
        /// <typeparam name="TEntry">The type to map the entry to.</typeparam>
        /// <param name="path">The path to get metadata for.</param>
        /// <param name="mapper">The mapper to transform FileSystemEntry to TEntry.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the mapped entry or an error.</returns>
        Task<Result<TEntry, Error>> GetEntryAsync<TEntry>(
            CanonicalPath path,
            IFileSystemEntryMapper<TEntry> mapper,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Traverses the file system starting from the specified path.
        /// </summary>
        /// <typeparam name="TEntry">The type of entries to map and handle.</typeparam>
        /// <param name="path">The starting path for traversal.</param>
        /// <param name="handler">The handler to process each mapped entry.</param>
        /// <param name="mapper">The mapper to transform file system entries to TEntry.</param>
        /// <param name="options">Optional traversal options. If null, uses default options.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing traversal summary or an error.</returns>
        Task<Result<TraversalSummary, Error>> TraverseAsync<TEntry>(
            CanonicalPath path,
            ITraversalHandler<TEntry> handler,
            IFileSystemEntryMapper<TEntry> mapper,
            TraversalOptions? options = null,
            CancellationToken cancellationToken = default);


        /// <summary>
        /// Creates a monitoring session for watching file system changes at the specified path.
        /// </summary>
        /// <param name="options">The monitoring options including path and filters.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the monitoring session or an error.</returns>
        Task<Result<IMonitoringSession, Error>> CreateMonitoringSessionAsync(MonitoringOptions options, CancellationToken cancellationToken = default);
    }
}
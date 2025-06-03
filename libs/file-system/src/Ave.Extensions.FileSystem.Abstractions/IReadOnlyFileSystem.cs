using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Provides a read-only cross-platform abstraction for file system operations.
    /// </summary>
    public interface IReadOnlyFileSystem
    {
        /// <summary>
        /// Determines whether the specified file exists.
        /// </summary>
        /// <param name="path">The file path to check.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating whether the file exists or an error occurred.</returns>
        Task<Result<bool, FileSystemError>> FileExistsAsync(CanonicalPath path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Determines whether the specified directory exists.
        /// </summary>
        /// <param name="path">The directory path to check.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating whether the directory exists or an error occurred.</returns>
        Task<Result<bool, FileSystemError>> DirectoryExistsAsync(CanonicalPath path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads all text from a file.
        /// </summary>
        /// <param name="path">The file path to read from.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the file content or an error.</returns>
        Task<Result<string, FileSystemError>> ReadAllTextAsync(CanonicalPath path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a file for reading.
        /// </summary>
        /// <param name="path">The file path to open.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the file stream or an error.</returns>
        Task<Result<Stream, FileSystemError>> OpenReadAsync(CanonicalPath path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets metadata for a file system entry (file or directory).
        /// </summary>
        /// <param name="path">The path to get metadata for.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the file system entry or an error.</returns>
        Task<Result<FileSystemEntry, FileSystemError>> GetEntryAsync(CanonicalPath path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a list of files in the specified directory.
        /// </summary>
        /// <param name="path">The directory path to enumerate.</param>
        /// <param name="searchPattern">The search string to match against files.</param>
        /// <param name="searchOption">Specifies whether to search subdirectories.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the file paths or an error.</returns>
        Task<Result<IReadOnlyList<CanonicalPath>, FileSystemError>> GetFilesAsync(CanonicalPath path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a list of directories in the specified directory.
        /// </summary>
        /// <param name="path">The directory path to enumerate.</param>
        /// <param name="searchPattern">The search string to match against directories.</param>
        /// <param name="searchOption">Specifies whether to search subdirectories.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the directory paths or an error.</returns>
        Task<Result<IReadOnlyList<CanonicalPath>, FileSystemError>> GetDirectoriesAsync(CanonicalPath path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly, CancellationToken cancellationToken = default);

        /// <summary>
        /// Enumerates file system entries asynchronously.
        /// </summary>
        /// <param name="path">The directory path to enumerate.</param>
        /// <param name="searchPattern">The search pattern to match.</param>
        /// <param name="options">Options for enumeration.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>An async enumerable of file system entries or errors.</returns>
        IAsyncEnumerable<Result<FileSystemEntry, FileSystemError>> EnumerateFileSystemEntriesAsync(
            CanonicalPath path,
            string searchPattern = "*",
            Models.EnumerationOptions? options = null,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Creates a file system monitor configured to use this file system's path handling.
        /// </summary>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A file system monitor instance.</returns>
        Task<IFileSystemMonitor> CreateMonitorAsync(CancellationToken cancellationToken = default);
    }
}
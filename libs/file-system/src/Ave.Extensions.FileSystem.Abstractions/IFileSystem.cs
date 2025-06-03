using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Provides a cross-platform abstraction for file system operations.
    /// </summary>
    public interface IFileSystem : IReadOnlyFileSystem
    {
        /// <summary>
        /// Creates a directory at the specified path.
        /// </summary>
        /// <param name="path">The directory path to create.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or failure.</returns>
        Task<Result<Unit, FileSystemError>> CreateDirectoryAsync(CanonicalPath path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes all text to a file, creating the file if it doesn't exist.
        /// </summary>
        /// <param name="path">The file path to write to.</param>
        /// <param name="contents">The text to write to the file.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or failure.</returns>
        Task<Result<Unit, FileSystemError>> WriteAllTextAsync(CanonicalPath path, string contents, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a file for writing.
        /// </summary>
        /// <param name="path">The file path to open.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the write stream or an error.</returns>
        Task<Result<Stream, FileSystemError>> OpenWriteAsync(CanonicalPath path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a file with specified mode, access, and share settings.
        /// </summary>
        /// <param name="path">The file path to open.</param>
        /// <param name="mode">The file mode.</param>
        /// <param name="access">The file access.</param>
        /// <param name="share">The file share.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the file stream or an error.</returns>
        Task<Result<Stream, FileSystemError>> OpenAsync(CanonicalPath path, FileMode mode, FileAccess access, FileShare share, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a file at the specified path.
        /// </summary>
        /// <param name="path">The file path to delete.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or failure.</returns>
        Task<Result<Unit, FileSystemError>> DeleteFileAsync(CanonicalPath path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a directory at the specified path.
        /// </summary>
        /// <param name="path">The directory path to delete.</param>
        /// <param name="recursive">Whether to recursively delete all contents.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or failure.</returns>
        Task<Result<Unit, FileSystemError>> DeleteDirectoryAsync(CanonicalPath path, bool recursive = false, CancellationToken cancellationToken = default);
    }
}

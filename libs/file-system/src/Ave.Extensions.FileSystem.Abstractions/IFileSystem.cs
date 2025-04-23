using System.Threading;
using System.Threading.Tasks;

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
        /// <returns>A task representing the asynchronous operation.</returns>
        Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes all text to a file, creating the file if it doesn't exist.
        /// </summary>
        /// <param name="path">The file path to write to.</param>
        /// <param name="contents">The text to write to the file.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a file at the specified path.
        /// </summary>
        /// <param name="path">The file path to delete.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task DeleteFileAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a directory at the specified path.
        /// </summary>
        /// <param name="path">The directory path to delete.</param>
        /// <param name="recursive">Whether to recursively delete all contents.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task DeleteDirectoryAsync(string path, bool recursive = false, CancellationToken cancellationToken = default);
    }
}

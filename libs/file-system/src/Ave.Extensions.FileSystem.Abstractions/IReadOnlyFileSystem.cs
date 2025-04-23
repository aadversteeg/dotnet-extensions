using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
        /// <returns>True if the file exists; otherwise, false.</returns>
        Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Determines whether the specified directory exists.
        /// </summary>
        /// <param name="path">The directory path to check.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>True if the directory exists; otherwise, false.</returns>
        Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads all text from a file.
        /// </summary>
        /// <param name="path">The file path to read from.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A string containing all text from the file.</returns>
        Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a list of files in the specified directory.
        /// </summary>
        /// <param name="path">The directory path to enumerate.</param>
        /// <param name="searchPattern">The search string to match against files.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A collection of file paths.</returns>
        Task<IEnumerable<string>> GetFilesAsync(string path, string searchPattern = "*", CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a list of directories in the specified directory.
        /// </summary>
        /// <param name="path">The directory path to enumerate.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A collection of directory paths.</returns>
        Task<IEnumerable<string>> GetDirectoriesAsync(string path, CancellationToken cancellationToken = default);
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Provides a cross-platform abstraction for file system operations.
    /// </summary>
    public interface IFileSystem
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
        /// Creates a directory at the specified path.
        /// </summary>
        /// <param name="path">The directory path to create.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads all text from a file.
        /// </summary>
        /// <param name="path">The file path to read from.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A string containing all text from the file.</returns>
        Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);

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

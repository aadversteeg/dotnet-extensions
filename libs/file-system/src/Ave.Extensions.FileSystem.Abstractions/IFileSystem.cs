using Ave.Extensions.ErrorPaths;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

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
        Task<Result<Unit, Error>> CreateDirectoryAsync(CanonicalPath path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes all text to a file, creating the file if it doesn't exist.
        /// </summary>
        /// <param name="path">The file path to write to.</param>
        /// <param name="contents">The text to write to the file.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or failure.</returns>
        Task<Result<Unit, Error>> WriteAllTextAsync(CanonicalPath path, string contents, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a file for writing.
        /// </summary>
        /// <param name="path">The file path to open.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the write stream or an error.</returns>
        Task<Result<Stream, Error>> OpenWriteAsync(CanonicalPath path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a file with specified mode, access, and share settings.
        /// </summary>
        /// <param name="path">The file path to open.</param>
        /// <param name="mode">The file mode.</param>
        /// <param name="access">The file access.</param>
        /// <param name="share">The file share.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the file stream or an error.</returns>
        Task<Result<Stream, Error>> OpenAsync(CanonicalPath path, FileMode mode, FileAccess access, FileShare share, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a file at the specified path.
        /// </summary>
        /// <param name="path">The file path to delete.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or failure.</returns>
        Task<Result<Unit, Error>> DeleteFileAsync(CanonicalPath path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a directory at the specified path.
        /// </summary>
        /// <param name="path">The directory path to delete.</param>
        /// <param name="recursive">Whether to recursively delete all contents.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or failure.</returns>
        Task<Result<Unit, Error>> DeleteDirectoryAsync(CanonicalPath path, bool recursive = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Moves a file from source to destination.
        /// </summary>
        /// <param name="source">The source file path.</param>
        /// <param name="destination">The destination file path.</param>
        /// <param name="overwrite">Whether to overwrite if destination exists.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or failure.</returns>
        /// <remarks>
        /// This operation is atomic where supported by the underlying file system.
        /// On the same volume, this is typically a metadata-only operation.
        /// File attributes and timestamps are preserved.
        /// Cross-volume moves may be implemented as copy+delete operations.
        /// </remarks>
        /// <example>
        /// <code>
        /// var result = await fileSystem.MoveFileAsync(
        ///     CanonicalPath.Create("/source/file.txt").Value,
        ///     CanonicalPath.Create("/dest/file.txt").Value,
        ///     overwrite: true);
        /// 
        /// if (result.IsSuccess)
        /// {
        ///     Console.WriteLine("File moved successfully");
        /// }
        /// else
        /// {
        ///     Console.WriteLine($"Move failed: {result.Error.Message}");
        /// }
        /// </code>
        /// </example>
        Task<Result<Unit, Error>> MoveFileAsync(
            CanonicalPath source, 
            CanonicalPath destination, 
            bool overwrite = false,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Moves a directory from source to destination.
        /// </summary>
        /// <param name="source">The source directory path.</param>
        /// <param name="destination">The destination directory path.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or failure.</returns>
        /// <remarks>
        /// This operation moves the entire directory tree recursively.
        /// If destination exists, operation fails unless it's empty.
        /// On same volume, this is an atomic metadata operation.
        /// Cross-volume moves require copying all contents and deleting source.
        /// </remarks>
        /// <example>
        /// <code>
        /// var result = await fileSystem.MoveDirectoryAsync(
        ///     CanonicalPath.Create("/old/location").Value,
        ///     CanonicalPath.Create("/new/location").Value);
        /// </code>
        /// </example>
        Task<Result<Unit, Error>> MoveDirectoryAsync(
            CanonicalPath source, 
            CanonicalPath destination,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Copies a file from source to destination.
        /// </summary>
        /// <param name="source">The source file path.</param>
        /// <param name="destination">The destination file path.</param>
        /// <param name="overwrite">Whether to overwrite if destination exists.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or failure.</returns>
        /// <remarks>
        /// This operation creates a complete copy of the source file.
        /// File attributes, timestamps, and permissions are preserved where possible.
        /// Large files are copied efficiently without loading entire content into memory.
        /// Operation can be cancelled for long-running copies.
        /// </remarks>
        /// <example>
        /// <code>
        /// var result = await fileSystem.CopyFileAsync(
        ///     CanonicalPath.Create("/source/document.pdf").Value,
        ///     CanonicalPath.Create("/backup/document.pdf").Value,
        ///     overwrite: true);
        /// </code>
        /// </example>
        Task<Result<Unit, Error>> CopyFileAsync(
            CanonicalPath source, 
            CanonicalPath destination, 
            bool overwrite = false,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Copies a directory recursively from source to destination.
        /// </summary>
        /// <param name="source">The source directory path.</param>
        /// <param name="destination">The destination directory path.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or failure.</returns>
        /// <remarks>
        /// This operation recursively copies all files and subdirectories.
        /// Directory structure is preserved including empty directories.
        /// File attributes and timestamps are preserved where possible.
        /// If destination exists, operation fails to prevent data loss.
        /// </remarks>
        /// <example>
        /// <code>
        /// var result = await fileSystem.CopyDirectoryAsync(
        ///     CanonicalPath.Create("/projects/current").Value,
        ///     CanonicalPath.Create("/backups/project-backup").Value);
        /// </code>
        /// </example>
        Task<Result<Unit, Error>> CopyDirectoryAsync(
            CanonicalPath source, 
            CanonicalPath destination,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Moves a file or directory from source to destination.
        /// Automatically detects whether source is a file or directory.
        /// </summary>
        /// <param name="source">The source path.</param>
        /// <param name="destination">The destination path.</param>
        /// <param name="overwrite">Whether to overwrite if destination exists (for files only).</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or failure.</returns>
        /// <remarks>
        /// This is a convenience method that automatically determines if source is a file or directory.
        /// For files, delegates to MoveFileAsync with overwrite parameter.
        /// For directories, delegates to MoveDirectoryAsync (overwrite not applicable).
        /// Provides a unified interface for move operations.
        /// </remarks>
        /// <example>
        /// <code>
        /// // Works for both files and directories
        /// var result = await fileSystem.MoveAsync(
        ///     CanonicalPath.Create("/source/item").Value,
        ///     CanonicalPath.Create("/dest/item").Value);
        /// </code>
        /// </example>
        Task<Result<Unit, Error>> MoveAsync(
            CanonicalPath source, 
            CanonicalPath destination, 
            bool overwrite = false,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Copies a file or directory from source to destination.
        /// Automatically detects whether source is a file or directory.
        /// </summary>
        /// <param name="source">The source path.</param>
        /// <param name="destination">The destination path.</param>
        /// <param name="overwrite">Whether to overwrite if destination exists (for files only).</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or failure.</returns>
        /// <remarks>
        /// This is a convenience method that automatically determines if source is a file or directory.
        /// For files, delegates to CopyFileAsync with overwrite parameter.
        /// For directories, delegates to CopyDirectoryAsync (overwrite not applicable).
        /// Provides a unified interface for copy operations.
        /// </remarks>
        /// <example>
        /// <code>
        /// // Works for both files and directories
        /// var result = await fileSystem.CopyAsync(
        ///     CanonicalPath.Create("/source/item").Value,
        ///     CanonicalPath.Create("/backup/item").Value);
        /// </code>
        /// </example>
        Task<Result<Unit, Error>> CopyAsync(
            CanonicalPath source, 
            CanonicalPath destination, 
            bool overwrite = false,
            CancellationToken cancellationToken = default);
    }
}

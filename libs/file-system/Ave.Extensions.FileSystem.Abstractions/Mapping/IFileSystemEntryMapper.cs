using Ave.Extensions.ErrorPaths;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Mapping
{
    /// <summary>
    /// Defines a mapper that transforms FileSystemEntry objects to custom types.
    /// </summary>
    /// <typeparam name="TEntry">The target type to map to.</typeparam>
    public interface IFileSystemEntryMapper<TEntry>
    {
        /// <summary>
        /// Maps a file entry to a custom type.
        /// </summary>
        /// <param name="entry">The file entry to map.</param>
        /// <param name="fileReader">A file reader for accessing file content.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the mapped entry or an error.</returns>
        Task<Result<TEntry, Error>> MapFileAsync(
            IFileEntry entry,
            IFileReader fileReader,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Maps a directory entry to a custom type.
        /// </summary>
        /// <param name="entry">The directory entry to map.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the mapped entry or an error.</returns>
        Task<Result<TEntry, Error>> MapDirectoryAsync(
            IDirectoryEntry entry,
            CancellationToken cancellationToken = default);
    }
}
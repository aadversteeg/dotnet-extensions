using System.Threading;
using System.Threading.Tasks;
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
        /// Maps a FileSystemEntry to a custom type.
        /// </summary>
        /// <param name="entry">The file system entry to map.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the mapped entry or an error.</returns>
        Task<Result<TEntry, FileSystemError>> MapAsync(
            FileSystemEntry entry,
            CancellationToken cancellationToken = default);
    }
}
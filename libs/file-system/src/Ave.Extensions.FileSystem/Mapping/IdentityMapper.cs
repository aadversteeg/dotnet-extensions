using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Mapping
{
    /// <summary>
    /// A no-op mapper that returns the FileSystemEntry unchanged.
    /// Use this when you don't need custom mapping.
    /// </summary>
    public class IdentityMapper : IFileSystemEntryMapper<FileSystemEntry>
    {
        /// <summary>
        /// Gets a singleton instance of the identity mapper.
        /// </summary>
        public static readonly IdentityMapper Instance = new IdentityMapper();

        /// <inheritdoc />
        public Task<Result<FileSystemEntry, FileSystemError>> MapAsync(
            FileSystemEntry entry,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<FileSystemEntry, FileSystemError>.Success(entry));
        }
    }
}
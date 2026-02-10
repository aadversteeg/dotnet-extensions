using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem.Abstractions;
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
        public Task<Result<FileSystemEntry, Error>> MapFileAsync(
            IFileEntry entry,
            IFileReader fileReader,
            CancellationToken cancellationToken = default)
        {
            // Convert from IFileEntry back to FileSystemEntry
            var result = new FileSystemEntry(
                path: entry.Path,
                isDirectory: false,
                size: entry.Info.Size,
                extension: entry.Info.Extension,
                createdUtc: entry.Info.Info.CreatedUtc,
                modifiedUtc: entry.Info.Info.ModifiedUtc,
                lastAccessUtc: entry.Info.Info.LastAccessUtc,
                attributes: entry.Info.Info.Attributes);
                
            return Task.FromResult(Result<FileSystemEntry, Error>.Success(result));
        }

        /// <inheritdoc />
        public Task<Result<FileSystemEntry, Error>> MapDirectoryAsync(
            IDirectoryEntry entry,
            CancellationToken cancellationToken = default)
        {
            // Convert from IDirectoryEntry back to FileSystemEntry
            var result = new FileSystemEntry(
                path: entry.Path,
                isDirectory: true,
                size: null,
                extension: null,
                createdUtc: entry.Info.Info.CreatedUtc,
                modifiedUtc: entry.Info.Info.ModifiedUtc,
                lastAccessUtc: entry.Info.Info.LastAccessUtc,
                attributes: entry.Info.Info.Attributes);
                
            return Task.FromResult(Result<FileSystemEntry, Error>.Success(result));
        }
    }
}
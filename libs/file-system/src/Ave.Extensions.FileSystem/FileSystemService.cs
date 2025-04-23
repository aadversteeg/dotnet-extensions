using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Default implementation of <see cref="IFileSystem"/> for cross-platform file operations.
    /// </summary>
    public class FileSystemService : IFileSystem
    {
        /// <inheritdoc />
        public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(File.Exists(path));
        }

        /// <inheritdoc />
        public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Directory.Exists(path));
        }

        /// <inheritdoc />
        public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(path);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        {
            return File.ReadAllTextAsync(path, cancellationToken);
        }

        /// <inheritdoc />
        public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
        {
            return File.WriteAllTextAsync(path, contents, cancellationToken);
        }

        /// <inheritdoc />
        public Task DeleteFileAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(path);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task DeleteDirectoryAsync(string path, bool recursive = false, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Delete(path, recursive);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IEnumerable<string>> GetFilesAsync(string path, string searchPattern = "*", CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Directory.GetFiles(path, searchPattern).AsEnumerable());
        }

        /// <inheritdoc />
        public Task<IEnumerable<string>> GetDirectoriesAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Directory.GetDirectories(path).AsEnumerable());
        }
        
        /// <inheritdoc />
        public Task<IFileSystemMonitor> CreateMonitorAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var monitor = new FileSystemMonitor(this);
            return Task.FromResult<IFileSystemMonitor>(monitor);
        }
        
        /// <summary>
        /// Normalizes a path for file system operations.
        /// </summary>
        /// <param name="path">The path to normalize.</param>
        /// <returns>The normalized path.</returns>
        internal string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
            
            return Path.GetFullPath(path);
        }
    }
}

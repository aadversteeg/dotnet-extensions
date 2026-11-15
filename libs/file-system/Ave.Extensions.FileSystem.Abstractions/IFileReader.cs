using Ave.Extensions.ErrorPaths;
using System.Threading;
using System.Threading.Tasks;

using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Abstractions
{
    /// <summary>
    /// Provides minimal file reading capabilities.
    /// </summary>
    public interface IFileReader
    {
        /// <summary>
        /// Reads bytes from a file.
        /// </summary>
        /// <param name="maxBytes">Maximum number of bytes to read.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the bytes read or an error.</returns>
        Task<Result<byte[], Error>> ReadBytesAsync(
            int maxBytes,
            CancellationToken cancellationToken = default);
    }
}
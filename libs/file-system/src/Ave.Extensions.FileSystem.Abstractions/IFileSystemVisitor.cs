using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Provides a visitor pattern for traversing file systems.
    /// </summary>
    public interface IFileSystemVisitor
    {
        /// <summary>
        /// Visits a file system tree starting from the specified root path.
        /// </summary>
        /// <param name="rootPath">The root canonical path to start visiting from.</param>
        /// <param name="options">Options controlling the visit behavior.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the visit summary or an error.</returns>
        Task<Result<FileSystemVisitResult, FileSystemError>> VisitAsync(
            CanonicalPath rootPath,
            FileSystemVisitorOptions? options = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Visits a file system tree as an async stream, yielding each visited item.
        /// </summary>
        /// <param name="rootPath">The root canonical path to start visiting from.</param>
        /// <param name="options">Options controlling the visit behavior.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>An async enumerable of visit events or errors.</returns>
        IAsyncEnumerable<Result<FileVisitedEventArgs, FileSystemError>> VisitStreamAsync(
            CanonicalPath rootPath,
            FileSystemVisitorOptions? options = null,
            CancellationToken cancellationToken = default);
    }
}
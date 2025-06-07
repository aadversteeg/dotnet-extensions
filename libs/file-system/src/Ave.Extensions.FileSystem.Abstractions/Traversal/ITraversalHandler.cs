using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Traversal
{
    /// <summary>
    /// Handles file system entries during traversal.
    /// </summary>
    /// <typeparam name="TEntry">The type of entries this handler processes.</typeparam>
    public interface ITraversalHandler<TEntry>
    {
        /// <summary>
        /// Called before traversal begins.
        /// </summary>
        /// <param name="rootPath">The root path where traversal will begin.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or an error.</returns>
        Task<Result<Unit, FileSystemError>> OnTraversalStartAsync(
            CanonicalPath rootPath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Called when entering a directory before processing its contents.
        /// </summary>
        /// <param name="directory">The mapped directory entry being entered.</param>
        /// <param name="context">The current traversal context.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing traversal control instructions or an error.</returns>
        Task<Result<TraversalControl, FileSystemError>> OnDirectoryEnterAsync(
            TEntry directory,
            TraversalContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Handles a file entry during traversal.
        /// </summary>
        /// <param name="file">The mapped file entry to handle.</param>
        /// <param name="context">The current traversal context.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing traversal control instructions or an error.</returns>
        Task<Result<TraversalControl, FileSystemError>> OnFileAsync(
            TEntry file,
            TraversalContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Handles a directory entry during traversal.
        /// </summary>
        /// <param name="directory">The mapped directory entry to handle.</param>
        /// <param name="context">The current traversal context.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing traversal control instructions or an error.</returns>
        Task<Result<TraversalControl, FileSystemError>> OnDirectoryAsync(
            TEntry directory,
            TraversalContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Called when traversal completes.
        /// </summary>
        /// <param name="summary">The summary of the traversal operation.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or an error.</returns>
        Task<Result<Unit, FileSystemError>> OnTraversalCompleteAsync(
            TraversalSummary summary,
            CancellationToken cancellationToken = default);
    }
}
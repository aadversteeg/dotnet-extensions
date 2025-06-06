using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Traversal.Handlers
{
    /// <summary>
    /// Base implementation of ITraversalHandler with virtual methods.
    /// </summary>
    public abstract class BaseTraversalHandler : ITraversalHandler
    {
        /// <summary>
        /// Called before traversal begins.
        /// </summary>
        /// <param name="rootPath">The root path where traversal will begin.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or an error.</returns>
        public virtual Task<Result<Unit, FileSystemError>> OnTraversalStartAsync(
            CanonicalPath rootPath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
        }

        /// <summary>
        /// Handles a file entry during traversal.
        /// </summary>
        /// <param name="file">The file entry to handle.</param>
        /// <param name="context">The current traversal context.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing traversal control instructions or an error.</returns>
        public abstract Task<Result<TraversalControl, FileSystemError>> HandleFileAsync(
            FileSystemEntry file,
            TraversalContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Handles a directory entry during traversal.
        /// </summary>
        /// <param name="directory">The directory entry to handle.</param>
        /// <param name="context">The current traversal context.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing traversal control instructions or an error.</returns>
        public abstract Task<Result<TraversalControl, FileSystemError>> HandleDirectoryAsync(
            FileSystemEntry directory,
            TraversalContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Called when traversal completes.
        /// </summary>
        /// <param name="summary">The summary of the traversal operation.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or an error.</returns>
        public virtual Task<Result<Unit, FileSystemError>> OnTraversalCompleteAsync(
            TraversalSummary summary,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
        }
    }
}
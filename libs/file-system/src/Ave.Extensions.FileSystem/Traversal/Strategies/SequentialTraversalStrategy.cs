using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Traversal.Strategies
{
    /// <summary>
    /// Sequential traversal strategy that processes entries one by one.
    /// </summary>
    public sealed class SequentialTraversalStrategy : BaseTraversalStrategy
    {
        /// <summary>
        /// Traverses the file system starting from the specified entry.
        /// </summary>
        /// <param name="entry">The entry to start traversal from.</param>
        /// <param name="handler">The handler to process each entry.</param>
        /// <param name="options">The traversal options.</param>
        /// <param name="context">The current traversal context.</param>
        /// <param name="stats">The traversal statistics.</param>
        /// <param name="getChildren">Function to get children of a directory.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or failure.</returns>
        public override async Task<Result<Unit, FileSystemError>> TraverseAsync(
            IFileSystemEntry entry,
            ITraversalHandler handler,
            TraversalOptions options,
            TraversalContext context,
            TraversalStats stats,
            Func<CanonicalPath, CancellationToken, Task<Result<IEnumerable<IFileSystemEntry>, FileSystemError>>> getChildren,
            CancellationToken cancellationToken)
        {
            if (entry is IDirectoryEntry directoryEntry)
            {
                stats.IncrementDirectories();
                
                // First call OnDirectoryEnterAsync
                var enterResult = await handler.OnDirectoryEnterAsync(directoryEntry, context, cancellationToken);
                if (enterResult.IsFailure)
                    return Result<Unit, FileSystemError>.Failure(enterResult.Error);

                var enterControl = enterResult.Value;
                if (enterControl.Action == TraversalAction.Stop)
                    return Result<Unit, FileSystemError>.Success(Unit.Value);

                // Then call OnDirectoryAsync
                var controlResult = await handler.OnDirectoryAsync(directoryEntry, context, cancellationToken);
                if (controlResult.IsFailure)
                    return Result<Unit, FileSystemError>.Failure(controlResult.Error);

                var control = controlResult.Value;
                
                if (control.Action == TraversalAction.Stop)
                    return Result<Unit, FileSystemError>.Success(Unit.Value);

                if (ShouldTraverseDirectory(context, options, control))
                {
                    var childrenResult = await getChildren(entry.Path, cancellationToken);
                    if (childrenResult.IsFailure)
                        return Result<Unit, FileSystemError>.Failure(childrenResult.Error);

                    foreach (var child in childrenResult.Value)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        var childContext = context.NextLevel(child.Path);
                        var result = await TraverseAsync(
                            child, handler, options, childContext, stats, getChildren, cancellationToken);
                        
                        if (result.IsFailure)
                            return result;
                    }
                }
            }
            else if (entry is IFileEntry fileEntry)
            {
                stats.IncrementFiles();
                stats.AddBytes(fileEntry.Info.Size);

                if (ShouldProcessEntry(entry, options))
                {
                    var controlResult = await handler.OnFileAsync(fileEntry, context, cancellationToken);
                    if (controlResult.IsFailure)
                        return Result<Unit, FileSystemError>.Failure(controlResult.Error);

                    if (controlResult.Value.Action == TraversalAction.Stop)
                        return Result<Unit, FileSystemError>.Success(Unit.Value);
                }
            }

            return Result<Unit, FileSystemError>.Success(Unit.Value);
        }
    }
}
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Traversal.Strategies
{
    /// <summary>
    /// Sequential traversal strategy that processes entries one by one.
    /// </summary>
    public class SequentialTraversalStrategy : BaseTraversalStrategy
    {
        /// <summary>
        /// Traverses the file system starting from the specified entry.
        /// </summary>
        /// <typeparam name="TEntry">The type of entries to map and handle.</typeparam>
        /// <param name="entry">The entry to start traversal from.</param>
        /// <param name="handler">The handler to process each mapped entry.</param>
        /// <param name="mapper">The mapper to transform file system entries to TEntry.</param>
        /// <param name="options">The traversal options.</param>
        /// <param name="context">The current traversal context.</param>
        /// <param name="stats">The traversal statistics.</param>
        /// <param name="getChildren">Function to get children of a directory.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result indicating success or failure.</returns>
        public override async Task<Result<Unit, Error>> TraverseAsync<TEntry>(
            IFileSystemEntry entry,
            ITraversalHandler<TEntry> handler,
            IFileSystemEntryMapper<TEntry> mapper,
            TraversalOptions options,
            TraversalContext context,
            TraversalStats stats,
            Func<CanonicalPath, CancellationToken, Task<Result<IEnumerable<IFileSystemEntry>, Error>>> getChildren,
            CancellationToken cancellationToken)
        {
            if (entry is IDirectoryEntry directoryEntry)
            {
                stats.IncrementDirectories();
                
                // Map the directory entry first
                var mappedDirectoryResult = await MapEntryAsync(directoryEntry, mapper, cancellationToken);
                if (mappedDirectoryResult.IsFailure)
                    return Result<Unit, Error>.Failure(mappedDirectoryResult.Error);

                var mappedDirectory = mappedDirectoryResult.Value;
                
                // First call OnDirectoryEnterAsync with mapped entry
                var enterResult = await handler.OnDirectoryEnterAsync(mappedDirectory, context, cancellationToken);
                if (enterResult.IsFailure)
                    return Result<Unit, Error>.Failure(enterResult.Error);

                var enterControl = enterResult.Value;
                if (enterControl.Action == TraversalAction.Stop)
                    return Result<Unit, Error>.Success(Unit.Value);

                // Then call OnDirectoryAsync with mapped entry
                var controlResult = await handler.OnDirectoryAsync(mappedDirectory, context, cancellationToken);
                if (controlResult.IsFailure)
                    return Result<Unit, Error>.Failure(controlResult.Error);

                var control = controlResult.Value;
                
                if (control.Action == TraversalAction.Stop)
                    return Result<Unit, Error>.Success(Unit.Value);

                if (ShouldTraverseDirectory(context, options, control))
                {
                    var childrenResult = await getChildren(entry.Path, cancellationToken);
                    if (childrenResult.IsFailure)
                        return Result<Unit, Error>.Failure(childrenResult.Error);

                    foreach (var child in childrenResult.Value)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        var childContext = context.NextLevel(child.Path);
                        var result = await TraverseAsync(
                            child, handler, mapper, options, childContext, stats, getChildren, cancellationToken);
                        
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
                    // Map the file entry first
                    var mappedFileResult = await MapEntryAsync(fileEntry, mapper, cancellationToken);
                    if (mappedFileResult.IsFailure)
                        return Result<Unit, Error>.Failure(mappedFileResult.Error);

                    var mappedFile = mappedFileResult.Value;
                    
                    var controlResult = await handler.OnFileAsync(mappedFile, context, cancellationToken);
                    if (controlResult.IsFailure)
                        return Result<Unit, Error>.Failure(controlResult.Error);

                    if (controlResult.Value.Action == TraversalAction.Stop)
                        return Result<Unit, Error>.Success(Unit.Value);
                }
            }

            return Result<Unit, Error>.Success(Unit.Value);
        }
    }
}
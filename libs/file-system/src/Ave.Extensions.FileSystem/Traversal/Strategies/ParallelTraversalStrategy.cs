using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Traversal.Strategies
{
    /// <summary>
    /// Traversal strategy that processes files and directories in parallel for improved performance.
    /// </summary>
    public class ParallelTraversalStrategy : BaseTraversalStrategy
    {
        private readonly int _maxConcurrency;
        private readonly SemaphoreSlim _semaphore;

        /// <summary>
        /// Initializes a new instance of the <see cref="ParallelTraversalStrategy"/> class.
        /// </summary>
        /// <param name="maxConcurrency">The maximum number of concurrent operations. Defaults to Environment.ProcessorCount * 2.</param>
        public ParallelTraversalStrategy(int? maxConcurrency = null)
        {
            _maxConcurrency = maxConcurrency ?? Environment.ProcessorCount * 2;
            _semaphore = new SemaphoreSlim(_maxConcurrency);
        }

        /// <inheritdoc />
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

                    var children = childrenResult.Value.ToList();
                    
                    var files = children.OfType<IFileEntry>().ToList();
                    var directories = children.OfType<IDirectoryEntry>().ToList();
                    
                    if (files.Any())
                    {
                        var fileTasks = files.Select(async file =>
                        {
                            await _semaphore.WaitAsync(cancellationToken);
                            try
                            {
                                stats.IncrementFiles();
                                stats.AddBytes(file.Info.Size);

                                if (ShouldProcessEntry(file, options))
                                {
                                    // Map the file entry first
                                    var mappedFileResult = await MapEntryAsync(file, mapper, cancellationToken);
                                    if (mappedFileResult.IsFailure)
                                        return false;

                                    var mappedFile = mappedFileResult.Value;
                                    var fileControl = await handler.OnFileAsync(mappedFile, context, cancellationToken);
                                    return fileControl.IsSuccess && fileControl.Value.Action != TraversalAction.Stop;
                                }
                                return true;
                            }
                            finally
                            {
                                _semaphore.Release();
                            }
                        });
                        
                        var fileResults = await Task.WhenAll(fileTasks);
                        if (fileResults.Any(r => !r))
                            return Result<Unit, Error>.Success(Unit.Value);
                    }
                    
                    if (directories.Any())
                    {
                        if (context.CurrentDepth < 2)
                        {
                            var dirTasks = directories.Select(async dir =>
                            {
                                await _semaphore.WaitAsync(cancellationToken);
                                try
                                {
                                    var childContext = context.NextLevel(dir.Path);
                                    var result = await TraverseAsync(
                                        dir, handler, mapper, options, childContext, stats, getChildren, cancellationToken);
                                    return result.IsSuccess;
                                }
                                finally
                                {
                                    _semaphore.Release();
                                }
                            });
                            
                            var dirResults = await Task.WhenAll(dirTasks);
                            if (dirResults.Any(r => !r))
                                return Result<Unit, Error>.Success(Unit.Value);
                        }
                        else
                        {
                            foreach (var dir in directories)
                            {
                                if (cancellationToken.IsCancellationRequested)
                                    break;
                                    
                                var childContext = context.NextLevel(dir.Path);
                                var result = await TraverseAsync(
                                    dir, handler, mapper, options, childContext, stats, getChildren, cancellationToken);
                                    
                                if (result.IsFailure)
                                    return result;
                            }
                        }
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
                }
            }

            return Result<Unit, Error>.Success(Unit.Value);
        }
    }
}
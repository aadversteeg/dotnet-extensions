using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Traversal.Strategies
{
    /// <summary>
    /// Traversal strategy that processes files and directories in parallel for improved performance.
    /// </summary>
    public sealed class ParallelTraversalStrategy : BaseTraversalStrategy
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
        public override async Task<Result<Unit, FileSystemError>> TraverseAsync(
            FileSystemEntry entry,
            ITraversalHandler handler,
            TraversalOptions options,
            TraversalContext context,
            TraversalStats stats,
            Func<CanonicalPath, CancellationToken, Task<Result<IEnumerable<FileSystemEntry>, FileSystemError>>> getChildren,
            CancellationToken cancellationToken)
        {
            if (entry.IsDirectory)
            {
                stats.IncrementDirectories();
                
                var controlResult = await handler.HandleDirectoryAsync(entry, context, cancellationToken);
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

                    var children = childrenResult.Value.ToList();
                    
                    var files = children.Where(c => !c.IsDirectory).ToList();
                    var directories = children.Where(c => c.IsDirectory).ToList();
                    
                    if (files.Any())
                    {
                        var fileTasks = files.Select(async file =>
                        {
                            await _semaphore.WaitAsync(cancellationToken);
                            try
                            {
                                stats.IncrementFiles();
                                if (file.Size.HasValue)
                                    stats.AddBytes(file.Size.Value);

                                if (ShouldProcessEntry(file, options))
                                {
                                    var fileControl = await handler.HandleFileAsync(file, context, cancellationToken);
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
                            return Result<Unit, FileSystemError>.Success(Unit.Value);
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
                                        dir, handler, options, childContext, stats, getChildren, cancellationToken);
                                    return result.IsSuccess;
                                }
                                finally
                                {
                                    _semaphore.Release();
                                }
                            });
                            
                            var dirResults = await Task.WhenAll(dirTasks);
                            if (dirResults.Any(r => !r))
                                return Result<Unit, FileSystemError>.Success(Unit.Value);
                        }
                        else
                        {
                            foreach (var dir in directories)
                            {
                                if (cancellationToken.IsCancellationRequested)
                                    break;
                                    
                                var childContext = context.NextLevel(dir.Path);
                                var result = await TraverseAsync(
                                    dir, handler, options, childContext, stats, getChildren, cancellationToken);
                                    
                                if (result.IsFailure)
                                    return result;
                            }
                        }
                    }
                }
            }
            else
            {
                stats.IncrementFiles();
                if (entry.Size.HasValue)
                    stats.AddBytes(entry.Size.Value);

                if (ShouldProcessEntry(entry, options))
                {
                    var controlResult = await handler.HandleFileAsync(entry, context, cancellationToken);
                    if (controlResult.IsFailure)
                        return Result<Unit, FileSystemError>.Failure(controlResult.Error);
                }
            }

            return Result<Unit, FileSystemError>.Success(Unit.Value);
        }
    }
}
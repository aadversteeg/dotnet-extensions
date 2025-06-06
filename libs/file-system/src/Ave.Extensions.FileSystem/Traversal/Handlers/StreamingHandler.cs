using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Traversal.Handlers
{
    /// <summary>
    /// Handler that streams results through a queue for IAsyncEnumerable support.
    /// </summary>
    internal sealed class StreamingHandler : ITraversalHandler
    {
        private readonly ConcurrentQueue<CanonicalPath> queue;
        private readonly bool includeFiles;
        private readonly bool includeDirectories;
        private readonly PatternMatcher matcher;
        private volatile bool isCompleted;
        private readonly TaskCompletionSource<bool> completionSource;

        public StreamingHandler(string searchPattern, bool includeFiles, bool includeDirectories)
        {
            this.includeFiles = includeFiles;
            this.includeDirectories = includeDirectories;
            this.matcher = new PatternMatcher(searchPattern);
            this.queue = new ConcurrentQueue<CanonicalPath>();
            this.completionSource = new TaskCompletionSource<bool>();
        }

        /// <summary>
        /// Returns an async enumerable that yields items as they're discovered.
        /// </summary>
        public async IAsyncEnumerable<CanonicalPath> GetItemsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (!isCompleted || !queue.IsEmpty)
            {
                if (queue.TryDequeue(out var item))
                {
                    yield return item;
                }
                else if (!isCompleted)
                {
                    await Task.Delay(1, cancellationToken);
                }
            }
        }

        public Task<Result<Unit, FileSystemError>> OnTraversalStartAsync(
            CanonicalPath rootPath,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
        }

        public Task<Result<TraversalControl, FileSystemError>> HandleFileAsync(
            FileSystemEntry file,
            TraversalContext context,
            CancellationToken cancellationToken)
        {
            if (includeFiles && matcher.IsMatch(file.Name))
            {
                queue.Enqueue(file.Path);
            }
            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }

        public Task<Result<TraversalControl, FileSystemError>> HandleDirectoryAsync(
            FileSystemEntry directory,
            TraversalContext context,
            CancellationToken cancellationToken)
        {
            if (includeDirectories && context.CurrentDepth > 0 && matcher.IsMatch(directory.Name))
            {
                queue.Enqueue(directory.Path);
            }
            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }

        public Task<Result<Unit, FileSystemError>> OnTraversalCompleteAsync(
            TraversalSummary summary,
            CancellationToken cancellationToken)
        {
            isCompleted = true;
            completionSource.SetResult(true);
            return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
        }
    }
}
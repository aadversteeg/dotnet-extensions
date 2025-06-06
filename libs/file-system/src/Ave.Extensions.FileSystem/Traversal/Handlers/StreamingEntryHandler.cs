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
    /// Handler that streams FileSystemEntry results for IAsyncEnumerable support.
    /// </summary>
    internal sealed class StreamingEntryHandler : ITraversalHandler
    {
        private readonly ConcurrentQueue<FileSystemEntry> queue;
        private readonly bool includeFiles;
        private readonly bool includeDirectories;
        private readonly PatternMatcher matcher;
        private volatile bool isCompleted;
        private readonly TaskCompletionSource<bool> completionSource;

        public StreamingEntryHandler(string searchPattern, bool includeFiles, bool includeDirectories)
        {
            this.includeFiles = includeFiles;
            this.includeDirectories = includeDirectories;
            this.matcher = new PatternMatcher(searchPattern);
            this.queue = new ConcurrentQueue<FileSystemEntry>();
            this.completionSource = new TaskCompletionSource<bool>();
        }

        public async IAsyncEnumerable<FileSystemEntry> GetEntriesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (!isCompleted || !queue.IsEmpty)
            {
                if (queue.TryDequeue(out var entry))
                {
                    yield return entry;
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
                queue.Enqueue(file);
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
                queue.Enqueue(directory);
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
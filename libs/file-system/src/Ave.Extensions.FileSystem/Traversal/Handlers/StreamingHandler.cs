using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Traversal.Handlers
{
    /// <summary>
    /// Handler that streams results through a queue for IAsyncEnumerable support.
    /// </summary>
    internal sealed class StreamingHandler : ITraversalHandler
    {
        private readonly ConcurrentQueue<CanonicalPath> _queue;
        private readonly bool _includeFiles;
        private readonly bool _includeDirectories;
        private readonly PatternMatcher _matcher;
        private volatile bool _isCompleted;
        private readonly TaskCompletionSource<bool> _completionSource;

        public StreamingHandler(string searchPattern, bool includeFiles, bool includeDirectories)
        {
            _includeFiles = includeFiles;
            _includeDirectories = includeDirectories;
            _matcher = new PatternMatcher(searchPattern);
            _queue = new ConcurrentQueue<CanonicalPath>();
            _completionSource = new TaskCompletionSource<bool>();
        }

        /// <summary>
        /// Returns an async enumerable that yields items as they're discovered.
        /// </summary>
        public async IAsyncEnumerable<CanonicalPath> GetItemsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (!_isCompleted || !_queue.IsEmpty)
            {
                if (_queue.TryDequeue(out var item))
                {
                    yield return item;
                }
                else if (!_isCompleted)
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

        public Task<Result<TraversalControl, FileSystemError>> OnDirectoryEnterAsync(
            IDirectoryEntry directory,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }

        public Task<Result<TraversalControl, FileSystemError>> OnFileAsync(
            IFileEntry file,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            if (_includeFiles && _matcher.IsMatch(file.Info.Info.Name))
            {
                _queue.Enqueue(file.Path);
            }
            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }

        public Task<Result<TraversalControl, FileSystemError>> OnDirectoryAsync(
            IDirectoryEntry directory,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            if (_includeDirectories && context.CurrentDepth > 0 && _matcher.IsMatch(directory.Info.Info.Name))
            {
                _queue.Enqueue(directory.Path);
            }
            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }

        public Task<Result<Unit, FileSystemError>> OnTraversalCompleteAsync(
            TraversalSummary summary,
            CancellationToken cancellationToken)
        {
            _isCompleted = true;
            _completionSource.SetResult(true);
            return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
        }
    }
}
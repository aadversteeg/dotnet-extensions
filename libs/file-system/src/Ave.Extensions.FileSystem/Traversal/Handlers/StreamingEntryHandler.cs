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
    /// Handler that streams FileSystemEntry results for IAsyncEnumerable support.
    /// </summary>
    internal sealed class StreamingEntryHandler : ITraversalHandler
    {
        private readonly ConcurrentQueue<FileSystemEntry> _queue;
        private readonly bool _includeFiles;
        private readonly bool _includeDirectories;
        private readonly PatternMatcher _matcher;
        private volatile bool _isCompleted;
        private readonly TaskCompletionSource<bool> _completionSource;

        public StreamingEntryHandler(string searchPattern, bool includeFiles, bool includeDirectories)
        {
            _includeFiles = includeFiles;
            _includeDirectories = includeDirectories;
            _matcher = new PatternMatcher(searchPattern);
            _queue = new ConcurrentQueue<FileSystemEntry>();
            _completionSource = new TaskCompletionSource<bool>();
        }

        public async IAsyncEnumerable<FileSystemEntry> GetEntriesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (!_isCompleted || !_queue.IsEmpty)
            {
                if (_queue.TryDequeue(out var entry))
                {
                    yield return entry;
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

        public Task<Result<TraversalControl, FileSystemError>> HandleFileAsync(
            FileSystemEntry file,
            TraversalContext context,
            CancellationToken cancellationToken)
        {
            if (_includeFiles && _matcher.IsMatch(file.Name))
            {
                _queue.Enqueue(file);
            }
            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }

        public Task<Result<TraversalControl, FileSystemError>> HandleDirectoryAsync(
            FileSystemEntry directory,
            TraversalContext context,
            CancellationToken cancellationToken)
        {
            if (_includeDirectories && context.CurrentDepth > 0 && _matcher.IsMatch(directory.Name))
            {
                _queue.Enqueue(directory);
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
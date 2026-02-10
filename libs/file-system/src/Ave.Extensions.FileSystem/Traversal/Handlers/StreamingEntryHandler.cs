using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Traversal.Handlers
{
    /// <summary>
    /// Handler that streams mapped entries for IAsyncEnumerable support.
    /// </summary>
    /// <typeparam name="TEntry">The type of entries this handler processes.</typeparam>
    internal sealed class StreamingEntryHandler<TEntry> : ITraversalHandler<TEntry>
    {
        private readonly ConcurrentQueue<TEntry> _queue;
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
            _queue = new ConcurrentQueue<TEntry>();
            _completionSource = new TaskCompletionSource<bool>();
        }

        public async IAsyncEnumerable<TEntry> GetEntriesAsync(
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

        public Task<Result<Unit, Error>> OnTraversalStartAsync(
            CanonicalPath rootPath,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
        }

        public Task<Result<TraversalControl, Error>> OnDirectoryEnterAsync(
            TEntry directory,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }

        public Task<Result<TraversalControl, Error>> OnFileAsync(
            TEntry file,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            // For generic entries, we need to check if they implement IFileSystemEntry for name matching
            if (_includeFiles)
            {
                if (file is IFileSystemEntry entryWithPath)
                {
                    if (_matcher.IsMatch(entryWithPath.Path.GetName()))
                    {
                        _queue.Enqueue(file);
                    }
                }
                else
                {
                    // If the entry doesn't have path info, just include it
                    _queue.Enqueue(file);
                }
            }
            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }

        public Task<Result<TraversalControl, Error>> OnDirectoryAsync(
            TEntry directory,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            if (_includeDirectories && context.CurrentDepth > 0)
            {
                if (directory is IFileSystemEntry entryWithPath)
                {
                    if (_matcher.IsMatch(entryWithPath.Path.GetName()))
                    {
                        _queue.Enqueue(directory);
                    }
                }
                else
                {
                    // If the entry doesn't have path info, just include it
                    _queue.Enqueue(directory);
                }
            }
            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }

        public Task<Result<Unit, Error>> OnTraversalCompleteAsync(
            TraversalSummary summary,
            CancellationToken cancellationToken)
        {
            _isCompleted = true;
            _completionSource.SetResult(true);
            return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
        }
    }
}
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
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
        private readonly Channel<TEntry> _channel;
        private readonly bool _includeFiles;
        private readonly bool _includeDirectories;
        private readonly PatternMatcher _matcher;

        public StreamingEntryHandler(string searchPattern, bool includeFiles, bool includeDirectories)
        {
            _includeFiles = includeFiles;
            _includeDirectories = includeDirectories;
            _matcher = new PatternMatcher(searchPattern);

            // Unbounded so a producing traversal never blocks on a slow consumer, which is what the
            // previous ConcurrentQueue gave us. Multiple writers because the parallel traversal
            // strategy calls the handler from several tasks at once; a single reader because the
            // enumerator returned by GetEntriesAsync is the only consumer.
            _channel = Channel.CreateUnbounded<TEntry>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        }

        public async IAsyncEnumerable<TEntry> GetEntriesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Waits for the producer instead of polling it: no Task.Delay timer-resolution floor
            // (~15.6ms per empty poll on Windows) and no busy loop when the traversal is slow.
            while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out var entry))
                {
                    yield return entry;
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
                        _channel.Writer.TryWrite(file);
                    }
                }
                else
                {
                    // If the entry doesn't have path info, just include it
                    _channel.Writer.TryWrite(file);
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
                        _channel.Writer.TryWrite(directory);
                    }
                }
                else
                {
                    // If the entry doesn't have path info, just include it
                    _channel.Writer.TryWrite(directory);
                }
            }
            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }

        public Task<Result<Unit, Error>> OnTraversalCompleteAsync(
            TraversalSummary summary,
            CancellationToken cancellationToken)
        {
            // TryComplete rather than Complete: completion is guaranteed by a finally in the file
            // system's TraverseAsync, so a handler may legitimately be told twice. The second call
            // must be a no-op, not an InvalidOperationException.
            _channel.Writer.TryComplete();
            return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
        }
    }
}
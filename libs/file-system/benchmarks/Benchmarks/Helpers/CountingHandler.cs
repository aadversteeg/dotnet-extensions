using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;

namespace Benchmarks.Helpers;

public class CountingHandler : ITraversalHandler<FileSystemEntry>
{
    public int FileCount { get; private set; }
    public int DirectoryCount { get; private set; }
    public long TotalSize { get; private set; }

    public Task<Result<Unit, Error>> OnTraversalStartAsync(
        CanonicalPath rootPath,
        CancellationToken cancellationToken = default)
    {
        FileCount = 0;
        DirectoryCount = 0;
        TotalSize = 0;
        return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
    }

    public Task<Result<TraversalControl, Error>> OnDirectoryEnterAsync(
        FileSystemEntry directory,
        TraversalContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
    }

    public Task<Result<TraversalControl, Error>> OnFileAsync(
        FileSystemEntry file,
        TraversalContext context,
        CancellationToken cancellationToken = default)
    {
        FileCount++;
        TotalSize += file.Size ?? 0;
        return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
    }

    public Task<Result<TraversalControl, Error>> OnDirectoryAsync(
        FileSystemEntry directory,
        TraversalContext context,
        CancellationToken cancellationToken = default)
    {
        DirectoryCount++;
        return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
    }

    public Task<Result<Unit, Error>> OnTraversalCompleteAsync(
        TraversalSummary summary,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
    }
}
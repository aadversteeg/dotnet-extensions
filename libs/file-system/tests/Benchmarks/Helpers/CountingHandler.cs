using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;

namespace Benchmarks.Helpers;

public class CountingHandler : ITraversalHandler
{
    public int FileCount { get; private set; }
    public int DirectoryCount { get; private set; }
    public long TotalSize { get; private set; }

    public Task<Result<Unit, FileSystemError>> OnTraversalStartAsync(
        CanonicalPath rootPath,
        CancellationToken cancellationToken = default)
    {
        FileCount = 0;
        DirectoryCount = 0;
        TotalSize = 0;
        return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
    }

    public Task<Result<TraversalControl, FileSystemError>> HandleFileAsync(
        FileSystemEntry file,
        TraversalContext context,
        CancellationToken cancellationToken = default)
    {
        FileCount++;
        TotalSize += file.Size ?? 0;
        return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
    }

    public Task<Result<TraversalControl, FileSystemError>> HandleDirectoryAsync(
        FileSystemEntry directory,
        TraversalContext context,
        CancellationToken cancellationToken = default)
    {
        DirectoryCount++;
        return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
    }

    public Task<Result<Unit, FileSystemError>> OnTraversalCompleteAsync(
        TraversalSummary summary,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
    }
}
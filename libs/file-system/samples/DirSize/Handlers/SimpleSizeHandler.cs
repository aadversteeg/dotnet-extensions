using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;
using DirSize.Models;
using DirSize.Services;

namespace DirSize.Handlers;

public class SimpleSizeHandler : ISizeHandler
{
    private readonly Dictionary<string, DirectoryData> _directorySizes = new();
    protected readonly Stack<string> _directoryStack = new();
    private readonly ConcurrentBag<string> _errors = new();
    private readonly Stopwatch _stopwatch = new();
    
    private long _totalSize;
    private int _totalFiles;
    private int _totalDirectories;

    public Dictionary<string, DirectoryData> DirectorySizes => _directorySizes;
    public long TotalSize => _totalSize;
    public int TotalFiles => _totalFiles;
    public int TotalDirectories => _totalDirectories;
    public int ErrorCount => _errors.Count;
    public IReadOnlyList<string> Errors => _errors.ToList();
    public TimeSpan ElapsedTime => _stopwatch.Elapsed;

    public virtual Task<Result<Unit, FileSystemError>> OnTraversalStartAsync(
        CanonicalPath rootPath, 
        CancellationToken cancellationToken = default)
    {
        _stopwatch.Start();
        _directoryStack.Push(rootPath.Value);
        _directorySizes[rootPath.Value] = new DirectoryData();
        return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
    }

    public virtual Task<Result<TraversalControl, FileSystemError>> OnDirectoryEnterAsync(
        FileSystemEntry directory, 
        TraversalContext context, 
        CancellationToken cancellationToken = default)
    {
        _directoryStack.Push(directory.Path.Value);
        _directorySizes[directory.Path.Value] = new DirectoryData();
        return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
    }

    public virtual Task<Result<TraversalControl, FileSystemError>> OnFileAsync(
        FileSystemEntry file, 
        TraversalContext context, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!file.IsDirectory)
            {
                var size = file.Size ?? 0;
                Interlocked.Add(ref _totalSize, size);
                Interlocked.Increment(ref _totalFiles);
                
                // Add size to all parent directories
                foreach (var dir in _directoryStack)
                {
                    if (_directorySizes.TryGetValue(dir, out var data))
                    {
                        data.TotalSize += size;
                        data.FileCount++;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _errors.Add($"Error processing file {file.Path.Value}: {ex.Message}");
        }
        
        return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
    }

    public virtual Task<Result<TraversalControl, FileSystemError>> OnDirectoryAsync(
        FileSystemEntry directory, 
        TraversalContext context, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Pop when leaving directory
            if (_directoryStack.Count > 0 && _directoryStack.Peek() == directory.Path.Value)
            {
                _directoryStack.Pop();
            }
            
            if (context.CurrentDepth > 0)
            {
                Interlocked.Increment(ref _totalDirectories);
            }
        }
        catch (Exception ex)
        {
            _errors.Add($"Error processing directory {directory.Path.Value}: {ex.Message}");
        }
        
        return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
    }

    public virtual Task<Result<Unit, FileSystemError>> OnTraversalCompleteAsync(
        TraversalSummary summary, 
        CancellationToken cancellationToken = default)
    {
        _stopwatch.Stop();
        return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
    }

    public virtual string GetSummary()
    {
        return $"Total: {SizeParser.FormatSize(TotalSize)} in {TotalFiles:N0} files, " +
               $"{TotalDirectories:N0} directories, processed in {SizeParser.FormatTime(ElapsedTime)}";
    }
}
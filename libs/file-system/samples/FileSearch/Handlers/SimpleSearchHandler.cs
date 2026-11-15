using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;
using FileSearch.Models;
using Microsoft.Extensions.Logging;

namespace FileSearch.Handlers;

public class SimpleSearchHandler : ISearchHandler
{
    protected readonly SearchCriteria _criteria;
    private readonly List<FoundFile> _foundFiles = new();
    private readonly ConcurrentBag<string> _errors = new();
    private readonly Stopwatch _stopwatch = new();
    protected readonly ILogger _logger;
    private int _filesScanned;
    private int _directoriesScanned;
    private bool _stoppedEarly;

    public List<FoundFile> FoundFiles => _foundFiles;
    public bool StoppedEarly => _stoppedEarly;
    public int FilesScanned => _filesScanned;
    public int DirectoriesScanned => _directoriesScanned;
    public int ErrorCount => _errors.Count;
    public IReadOnlyList<string> Errors => _errors.ToList();
    public TimeSpan ElapsedTime => _stopwatch.Elapsed;
    public SearchCriteria Criteria => _criteria;

    public SimpleSearchHandler(SearchCriteria criteria, ILogger<SimpleSearchHandler> logger)
    {
        _criteria = criteria;
        _logger = logger;
    }

    public virtual Task<Result<Unit, Error>> OnTraversalStartAsync(
        CanonicalPath rootPath, 
        CancellationToken cancellationToken = default)
    {
        _stopwatch.Start();
        _logger.LogInformation("Starting search traversal from: {RootPath}", rootPath.Value);
        return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
    }

    public virtual Task<Result<TraversalControl, Error>> OnDirectoryEnterAsync(
        FileSystemEntry directory, 
        TraversalContext context, 
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _directoriesScanned);

        if (!_criteria.IncludeHidden && IsHidden(directory))
        {
            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Skip));
        }

        return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
    }

    public virtual Task<Result<TraversalControl, Error>> OnFileAsync(
        FileSystemEntry file,
        TraversalContext context,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _filesScanned);

        if (_foundFiles.Count >= _criteria.MaxResults)
        {
            _stoppedEarly = true;
            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Stop));
        }

        if (MatchesFile(file))
        {
            lock (_foundFiles)
            {
                if (_foundFiles.Count < _criteria.MaxResults)
                {
                    _foundFiles.Add(CreateFoundFile(file));
                }
            }
        }

        return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
    }

    public virtual Task<Result<TraversalControl, Error>> OnDirectoryAsync(
        FileSystemEntry directory, 
        TraversalContext context, 
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
    }

    public virtual Task<Result<Unit, Error>> OnTraversalCompleteAsync(
        TraversalSummary summary, 
        CancellationToken cancellationToken = default)
    {
        _stopwatch.Stop();
        _logger.LogInformation("Search traversal completed in {ElapsedTime}", _stopwatch.Elapsed);
        return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
    }

    public virtual Task<Result<TraversalControl, Error>> OnErrorAsync(
        Error error,
        CanonicalPath path,
        TraversalContext context, 
        CancellationToken cancellationToken = default)
    {
        _errors.Add($"{path.Value}: {error.Message}");
        _logger.LogDebug("Traversal error at {Path}: {Error}", path.Value, error.Message);
        return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
    }

    protected virtual bool MatchesFile(FileSystemEntry file)
    {
        if (file.IsDirectory)
            return false;

        if (!_criteria.IncludeHidden && IsHidden(file))
            return false;

        var size = file.Size ?? 0;
        if (_criteria.LargerThan.HasValue && size <= _criteria.LargerThan.Value)
            return false;
        if (_criteria.SmallerThan.HasValue && size >= _criteria.SmallerThan.Value)
            return false;

        if (_criteria.ModifiedAfter.HasValue && file.ModifiedUtc < _criteria.ModifiedAfter.Value)
            return false;
        if (_criteria.ModifiedBefore.HasValue && file.ModifiedUtc > _criteria.ModifiedBefore.Value)
            return false;
        if (_criteria.CreatedAfter.HasValue && file.CreatedUtc < _criteria.CreatedAfter.Value)
            return false;
        if (_criteria.CreatedBefore.HasValue && file.CreatedUtc > _criteria.CreatedBefore.Value)
            return false;

        var fileName = file.Name;
        if (_criteria.CompiledRegex != null)
        {
            if (!_criteria.CompiledRegex.IsMatch(fileName))
                return false;
        }
        else if (!MatchesPattern(fileName, _criteria.Pattern))
        {
            return false;
        }

        return true;
    }

    protected virtual bool IsHidden(FileSystemEntry entry)
    {
        if (entry.Name.StartsWith('.'))
            return true;

        if ((entry.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
            return true;

        return false;
    }

    protected virtual bool MatchesPattern(string fileName, string pattern)
    {
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        
        var options = _criteria.CaseSensitive 
            ? System.Text.RegularExpressions.RegexOptions.None 
            : System.Text.RegularExpressions.RegexOptions.IgnoreCase;
            
        return System.Text.RegularExpressions.Regex.IsMatch(fileName, regexPattern, options);
    }

    protected virtual FoundFile CreateFoundFile(FileSystemEntry file)
    {
        return new FoundFile
        {
            Path = file.Path.Value,
            Name = file.Name,
            Size = file.Size ?? 0,
            Extension = Path.GetExtension(file.Name),
            ModifiedUtc = file.ModifiedUtc,
            CreatedUtc = file.CreatedUtc,
            Attributes = file.Attributes
        };
    }

    public virtual string GetSummary()
    {
        return $"Found {_foundFiles.Count} file(s) in {_stopwatch.Elapsed.TotalSeconds:F2} seconds. " +
               $"Scanned {_filesScanned:N0} files and {_directoriesScanned:N0} directories.";
    }
}
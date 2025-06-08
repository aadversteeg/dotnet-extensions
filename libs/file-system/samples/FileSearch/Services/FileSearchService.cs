using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;
using FileSearch.Handlers;
using FileSearch.Models;
using Microsoft.Extensions.Logging;

namespace FileSearch.Services;

public class FileSearchService
{
    private readonly IFileSystem _fileSystem;
    private readonly HandlerFactory _handlerFactory;
    private readonly IPathMapper _pathMapper;
    private readonly SizeParser _sizeParser;
    private readonly PatternMatcher _patternMatcher;
    private readonly ILogger<FileSearchService> _logger;

    public FileSearchService(
        IFileSystem fileSystem,
        HandlerFactory handlerFactory,
        IPathMapper pathMapper,
        SizeParser sizeParser,
        PatternMatcher patternMatcher,
        ILogger<FileSearchService> logger)
    {
        _fileSystem = fileSystem;
        _handlerFactory = handlerFactory;
        _pathMapper = pathMapper;
        _sizeParser = sizeParser;
        _patternMatcher = patternMatcher;
        _logger = logger;
    }

    public async Task<Result<ISearchHandler, FileSystemError>> SearchAsync(
        SearchOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting file search in: {Path}", options.Path);

            var criteria = BuildSearchCriteria(options);
            
            var absolutePath = Path.GetFullPath(options.Path);
            _logger.LogInformation("Absolute path resolved to: {AbsolutePath}", absolutePath);
            
            var canonicalResult = _pathMapper.MapToCanonicalPath(absolutePath);
            if (canonicalResult.IsFailure)
            {
                _logger.LogError("Invalid path: {Error}", canonicalResult.Error.Message);
                return Result<ISearchHandler, FileSystemError>.Failure(
                    new FileSystemError("InvalidPath", canonicalResult.Error.Message));
            }
            
            _logger.LogInformation("Canonical path resolved to: {CanonicalPath}", canonicalResult.Value.Value);

            var handler = _handlerFactory.CreateHandler(options.HandlerType, criteria);
            var mapper = IdentityMapper.Instance;

            var traversalOptions = new TraversalOptions(
                maxDepth: options.MaxDepth,
                recursive: true,
                strategyHint: options.Strategy);

            var result = await _fileSystem.TraverseAsync(
                canonicalResult.Value,
                handler,
                mapper,
                traversalOptions,
                cancellationToken);

            if (result.IsSuccess)
            {
                if (handler.ErrorCount > 0)
                {
                    _logger.LogWarning("Search completed with {ErrorCount} errors", handler.ErrorCount);
                }
                return Result<ISearchHandler, FileSystemError>.Success(handler);
            }
            else if (result.Error.Type == FileSystemErrorType.AccessDenied && handler.FoundFiles.Count > 0)
            {
                _logger.LogWarning("Search completed with access denied, partial results available");
                return Result<ISearchHandler, FileSystemError>.Success(handler);
            }
            else
            {
                _logger.LogError("Search failed: {Error}", result.Error.Message);
                return Result<ISearchHandler, FileSystemError>.Failure(result.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during file search");
            return Result<ISearchHandler, FileSystemError>.Failure(
                new FileSystemError("UnexpectedError", ex.Message));
        }
    }

    private SearchCriteria BuildSearchCriteria(SearchOptions options)
    {
        var largerThan = options.LargerThan != null 
            ? _sizeParser.Parse(options.LargerThan) 
            : null;
        var smallerThan = options.SmallerThan != null 
            ? _sizeParser.Parse(options.SmallerThan) 
            : null;

        Regex? compiledRegex = null;
        if (!string.IsNullOrEmpty(options.RegexPattern))
        {
            var regexOptions = options.CaseSensitive 
                ? RegexOptions.None 
                : RegexOptions.IgnoreCase;
            compiledRegex = new Regex(options.RegexPattern, 
                regexOptions | RegexOptions.Compiled);
        }

        return new SearchCriteria
        {
            Path = options.Path,
            Pattern = options.Pattern,
            RegexPattern = options.RegexPattern,
            CompiledRegex = compiledRegex,
            ModifiedAfter = options.ModifiedAfter,
            ModifiedBefore = options.ModifiedBefore,
            CreatedAfter = options.CreatedAfter,
            CreatedBefore = options.CreatedBefore,
            LargerThan = largerThan,
            SmallerThan = smallerThan,
            MaxResults = options.MaxResults,
            IncludeHidden = options.IncludeHidden,
            CaseSensitive = options.CaseSensitive,
            MaxDepth = options.MaxDepth
        };
    }
}
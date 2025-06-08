using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.MimeDetective.Configuration;
using Ave.Extensions.FileSystem.MimeDetective.Mapping;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;
using ContentTypeAnalyzer.Handlers;
using ContentTypeAnalyzer.Models;
using Microsoft.Extensions.Logging;
using MimeDetective;

namespace ContentTypeAnalyzer.Services
{
    public class ContentAnalyzerService
    {
        private readonly IFileSystem _fileSystem;
        private readonly IPathMapper _pathMapper;
        private readonly HandlerFactory _handlerFactory;
        private readonly ILogger<ContentAnalyzerService> _logger;

        public ContentAnalyzerService(
            IFileSystem fileSystem,
            IPathMapper pathMapper,
            HandlerFactory handlerFactory,
            ILogger<ContentAnalyzerService> logger)
        {
            _fileSystem = fileSystem;
            _pathMapper = pathMapper;
            _handlerFactory = handlerFactory;
            _logger = logger;
        }

        public async Task<Result<IContentTypeHandler, FileSystemError>> AnalyzeAsync(
            AnalysisOptions options, 
            CancellationToken cancellationToken = default)
        {
            var absolutePath = Path.GetFullPath(options.Path);
            _logger.LogInformation("Absolute path: {Path}", absolutePath);

            var canonicalResult = _pathMapper.MapToCanonicalPath(absolutePath);
            if (canonicalResult.IsFailure)
            {
                return Result<IContentTypeHandler, FileSystemError>.Failure(
                    new FileSystemError("InvalidPath", canonicalResult.Error.Message));
            }

            _logger.LogInformation("Canonical path: {Path}", canonicalResult.Value.Value);

            var handler = _handlerFactory.CreateHandler(options.HandlerType, options);
            
            var inspector = ContentInspectorFactory.Create(options.DefinitionPack, options.UsageType);
            var mapper = new ContentTypeMapper(inspector, new ContentTypeMapperOptions
            {
                MaxBytesToRead = options.MaxBytes,
                FallbackToExtension = options.FallbackExtension,
                DefaultContentType = "application/octet-stream"
            });

            var traversalOptions = new TraversalOptions(
                maxDepth: options.MaxDepth,
                recursive: true,
                strategyHint: options.Strategy);

            _logger.LogInformation("Starting content type analysis...");
            
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
                    _logger.LogWarning("Analysis completed with {ErrorCount} errors", handler.ErrorCount);
                }
                return Result<IContentTypeHandler, FileSystemError>.Success(handler);
            }
            else if (result.Error.Type == FileSystemErrorType.AccessDenied && handler.AnalyzedFiles > 0)
            {
                _logger.LogWarning("Analysis completed with access denied, partial results available");
                return Result<IContentTypeHandler, FileSystemError>.Success(handler);
            }
            else
            {
                _logger.LogError("Analysis failed: {Error}", result.Error.Message);
                return Result<IContentTypeHandler, FileSystemError>.Failure(result.Error);
            }
        }
    }
}
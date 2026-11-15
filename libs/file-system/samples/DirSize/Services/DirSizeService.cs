using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem.Traversal;
using DirSize.Export;
using DirSize.Handlers;
using DirSize.Models;
using FileSystemSamples.Common.Options;
using Microsoft.Extensions.Logging;

namespace DirSize.Services;

public class DirSizeService
{
    private readonly IFileSystem _fileSystem;
    private readonly HandlerFactory _handlerFactory;
    private readonly IPathMapper _pathMapper;
    private readonly ILogger<DirSizeService> _logger;

    public DirSizeService(
        IFileSystem fileSystem, 
        HandlerFactory handlerFactory, 
        IPathMapper pathMapper,
        ILogger<DirSizeService> logger)
    {
        _fileSystem = fileSystem;
        _handlerFactory = handlerFactory;
        _pathMapper = pathMapper;
        _logger = logger;
    }

    public async Task<int> ExecuteAsync(DirSizeOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Analyzing directory sizes in: {Path}", options.Path);

            // Convert to canonical path using the proper path mapper
            var absolutePath = Path.GetFullPath(options.Path);
            _logger.LogInformation("Absolute path resolved to: {AbsolutePath}", absolutePath);
            
            var canonicalResult = _pathMapper.MapToCanonicalPath(absolutePath);
            if (canonicalResult.IsFailure)
            {
                _logger.LogError("Invalid path: {Error}", canonicalResult.Error.Message);
                return 1;
            }
            
            _logger.LogInformation("Canonical path resolved to: {CanonicalPath}", canonicalResult.Value.Value);

            // Create size handler
            var handler = _handlerFactory.CreateHandler(options.HandlerType);
            var mapper = new IdentityMapper();

            // Configure traversal options
            var traversalOptions = new TraversalOptions(
                maxDepth: options.MaxDepth,
                recursive: true,
                strategyHint: options.Strategy);

            // Perform traversal
            var result = await _fileSystem.TraverseAsync(
                canonicalResult.Value,
                handler,
                mapper,
                traversalOptions,
                cancellationToken);

            if (result.IsSuccess)
            {
                var message = handler.ErrorCount > 0 
                    ? $"Directory analysis completed with {handler.ErrorCount} errors (access denied, etc.)"
                    : "Directory analysis completed successfully";
                _logger.LogInformation(message);
                
                // Handle tree output differently
                if (options.HandlerType == HandlerType.Tree && handler is TreeSizeHandler treeHandler)
                {
                    if (options.ExportFormat == ExportFormat.Text && string.IsNullOrEmpty(options.OutputPath))
                    {
                        // Print tree to console
                        treeHandler.PrintTree();
                        Console.WriteLine();
                        Console.WriteLine(handler.GetSummary());
                        return 0;
                    }
                }

                // Export results
                var content = await DirSizeExporter.ExportAsync(handler, options, options.ExportFormat, options.OutputPath);
                
                if (content != null)
                {
                    // Output to console
                    Console.WriteLine(content);
                }
                else if (!string.IsNullOrEmpty(options.OutputPath))
                {
                    _logger.LogInformation("Results exported to: {OutputPath}", options.OutputPath);
                }

                return 0;
            }
            else
            {
                // Check if this is an access denied error and we still got some results
                if (result.Error.Is(FileSystemErrorCodes.IO.AccessDenied) && (handler.TotalFiles > 0 || handler.TotalDirectories > 0))
                {
                    _logger.LogWarning("Traversal completed with access denied error, but partial results available: {Error}", result.Error.Message);
                    
                    // Handle tree output differently
                    if (options.HandlerType == HandlerType.Tree && handler is TreeSizeHandler treeHandler)
                    {
                        if (options.ExportFormat == ExportFormat.Text && string.IsNullOrEmpty(options.OutputPath))
                        {
                            // Print tree to console
                            treeHandler.PrintTree();
                            Console.WriteLine();
                            Console.WriteLine(handler.GetSummary());
                            return 0;
                        }
                    }

                    // Export partial results
                    var content = await DirSizeExporter.ExportAsync(handler, options, options.ExportFormat, options.OutputPath);
                    
                    if (content != null)
                    {
                        // Output to console
                        Console.WriteLine(content);
                    }
                    else if (!string.IsNullOrEmpty(options.OutputPath))
                    {
                        _logger.LogInformation("Results exported to: {OutputPath}", options.OutputPath);
                    }

                    return 0;
                }
                
                _logger.LogError("Traversal failed: {Error}", result.Error.Message);
                return 1;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Operation was cancelled");
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during size calculation");
            return 1;
        }
    }

}
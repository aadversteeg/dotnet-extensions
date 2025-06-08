using System;
using FileSearch.Models;
using Microsoft.Extensions.Logging;

namespace FileSearch.Handlers;

public class HandlerFactory
{
    private readonly ILogger<HandlerFactory> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public HandlerFactory(ILogger<HandlerFactory> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public ISearchHandler CreateHandler(HandlerType handlerType, SearchCriteria criteria)
    {
        _logger.LogDebug("Creating {HandlerType} handler", handlerType);

        return handlerType switch
        {
            HandlerType.Simple => new SimpleSearchHandler(criteria, _loggerFactory.CreateLogger<SimpleSearchHandler>()),
            HandlerType.Detailed => new DetailedSearchHandler(criteria, _loggerFactory.CreateLogger<DetailedSearchHandler>()),
            HandlerType.Metadata => new MetadataSearchHandler(criteria, _loggerFactory.CreateLogger<MetadataSearchHandler>()),
            _ => throw new ArgumentException($"Unknown handler type: {handlerType}", nameof(handlerType))
        };
    }
}
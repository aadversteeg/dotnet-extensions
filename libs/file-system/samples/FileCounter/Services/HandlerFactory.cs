using System;
using System.Collections.Generic;
using FileCounter.Handlers;
using Microsoft.Extensions.Logging;

namespace FileCounter.Services
{
    /// <summary>
    /// Factory for creating counting handlers based on handler type and options.
    /// </summary>
    public class HandlerFactory
    {
        private readonly ILogger<HandlerFactory> _logger;

        /// <summary>
        /// Initializes a new instance of the HandlerFactory class.
        /// </summary>
        /// <param name="logger">Logger for debug information.</param>
        public HandlerFactory(ILogger<HandlerFactory> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Creates a counting handler based on the specified type and options.
        /// </summary>
        /// <param name="handlerType">The type of handler to create.</param>
        /// <param name="options">The counting options to configure the handler.</param>
        /// <returns>A configured counting handler instance.</returns>
        /// <exception cref="ArgumentException">Thrown when the handler type is not supported.</exception>
        public ICountingHandler CreateHandler(HandlerType handlerType, CountingOptions options)
        {
            return handlerType switch
            {
                HandlerType.Simple => new SimpleCounterHandler(
                    options.ExtensionFilter,
                    options.MinSize,
                    options.MaxSize,
                    _logger),

                HandlerType.Detailed => new DetailedCounterHandler(
                    options.ExtensionFilter,
                    options.MinSize,
                    options.MaxSize,
                    options.MaxExtensions,
                    _logger),

                HandlerType.Summary => new SummaryCounterHandler(
                    options.ExtensionFilter,
                    options.MinSize,
                    options.MaxSize,
                    _logger),

                _ => throw new ArgumentException($"Unsupported handler type: {handlerType}", nameof(handlerType))
            };
        }

        /// <summary>
        /// Gets the available handler types with their descriptions.
        /// </summary>
        /// <returns>A dictionary mapping handler types to their descriptions.</returns>
        public static IReadOnlyDictionary<HandlerType, string> GetHandlerDescriptions()
        {
            return new Dictionary<HandlerType, string>
            {
                [HandlerType.Simple] = "Basic counting of files and directories with minimal memory footprint",
                [HandlerType.Detailed] = "Extended counting with per-extension breakdown and size distribution",
                [HandlerType.Summary] = "Comprehensive statistics including performance metrics and file analysis"
            };
        }
    }
}
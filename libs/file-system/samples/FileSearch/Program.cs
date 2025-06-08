using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Traversal;
using FileSearch.Export;
using FileSearch.Handlers;
using FileSearch.Models;
using FileSearch.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FileSearch;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Search for files with various criteria");

        var pathOption = new Option<string>(
            aliases: new[] { "--path", "-p" },
            description: "Path to search in",
            getDefaultValue: () => Directory.GetCurrentDirectory());

        var patternOption = new Option<string>(
            aliases: new[] { "--pattern", "-n" },
            description: "File name pattern with wildcards (* and ?)",
            getDefaultValue: () => "*");

        var regexOption = new Option<string?>(
            aliases: new[] { "--regex", "-r" },
            description: "Regular expression for file names (overrides pattern)");

        var modifiedAfterOption = new Option<DateTime?>(
            aliases: new[] { "--modified-after", "-ma" },
            description: "Files modified after this date (ISO format)");

        var modifiedBeforeOption = new Option<DateTime?>(
            aliases: new[] { "--modified-before", "-mb" },
            description: "Files modified before this date (ISO format)");

        var createdAfterOption = new Option<DateTime?>(
            aliases: new[] { "--created-after", "-ca" },
            description: "Files created after this date (ISO format)");

        var createdBeforeOption = new Option<DateTime?>(
            aliases: new[] { "--created-before", "-cb" },
            description: "Files created before this date (ISO format)");

        var largerThanOption = new Option<string?>(
            aliases: new[] { "--larger-than", "-l" },
            description: "Files larger than size (e.g., 1MB)");

        var smallerThanOption = new Option<string?>(
            aliases: new[] { "--smaller-than", "-s" },
            description: "Files smaller than size (e.g., 1MB)");

        var maxResultsOption = new Option<int>(
            aliases: new[] { "--max-results", "-m" },
            description: "Maximum number of results",
            getDefaultValue: () => 100);

        var includeHiddenOption = new Option<bool>(
            aliases: new[] { "--include-hidden", "-h" },
            description: "Include hidden files",
            getDefaultValue: () => false);

        var caseSensitiveOption = new Option<bool>(
            aliases: new[] { "--case-sensitive", "-c" },
            description: "Case-sensitive pattern matching",
            getDefaultValue: () => false);

        var maxDepthOption = new Option<int?>(
            aliases: new[] { "--max-depth" },
            description: "Maximum traversal depth (null = unlimited)");

        var strategyOption = new Option<TraversalStrategyHint>(
            aliases: new[] { "--strategy" },
            description: "Traversal strategy: Auto, Search, FullScan, MemoryEfficient, Parallel",
            getDefaultValue: () => TraversalStrategyHint.Search);

        var handlerOption = new Option<HandlerType>(
            aliases: new[] { "--handler" },
            description: "Handler type: Simple, Detailed, Metadata",
            getDefaultValue: () => HandlerType.Simple);

        var exportOption = new Option<ExportFormat>(
            aliases: new[] { "--format" },
            description: "Output format: Text, Json, Csv, Xml, Markdown",
            getDefaultValue: () => ExportFormat.Text);

        var outputOption = new Option<string?>(
            aliases: new[] { "--output", "-o" },
            description: "Output file path (uses stdout if not specified)");

        var verboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "Show detailed error information");

        var logLevelOption = new Option<LogLevel>(
            aliases: new[] { "--log-level", "--loglevel" },
            description: "Logging level: Trace, Debug, Information, Warning, Error, Critical",
            getDefaultValue: () => LogLevel.Warning);

        rootCommand.AddOption(pathOption);
        rootCommand.AddOption(patternOption);
        rootCommand.AddOption(regexOption);
        rootCommand.AddOption(modifiedAfterOption);
        rootCommand.AddOption(modifiedBeforeOption);
        rootCommand.AddOption(createdAfterOption);
        rootCommand.AddOption(createdBeforeOption);
        rootCommand.AddOption(largerThanOption);
        rootCommand.AddOption(smallerThanOption);
        rootCommand.AddOption(maxResultsOption);
        rootCommand.AddOption(includeHiddenOption);
        rootCommand.AddOption(caseSensitiveOption);
        rootCommand.AddOption(maxDepthOption);
        rootCommand.AddOption(strategyOption);
        rootCommand.AddOption(handlerOption);
        rootCommand.AddOption(exportOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(verboseOption);
        rootCommand.AddOption(logLevelOption);

        rootCommand.SetHandler(async (InvocationContext context) =>
        {
            var logLevel = context.ParseResult.GetValueForOption(logLevelOption);
            
            var actualHostBuilder = Host.CreateDefaultBuilder()
                .ConfigureLogging((hostContext, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConsole(options =>
                    {
                        options.LogToStandardErrorThreshold = LogLevel.Trace;
                    });
                    logging.SetMinimumLevel(logLevel);
                    logging.AddFilter("Microsoft", LogLevel.Warning);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddFileSystem();
                    services.AddSingleton<HandlerFactory>();
                    services.AddScoped<FileSearchService>();
                    services.AddSingleton<SizeParser>();
                    services.AddSingleton<Services.PatternMatcher>();
                });

            var host = actualHostBuilder.Build();
            var searchService = host.Services.GetRequiredService<FileSearchService>();
            var sizeParser = host.Services.GetRequiredService<SizeParser>();
            var logger = host.Services.GetRequiredService<ILogger<Program>>();

            var searchOptions = new SearchOptions
            {
                Path = context.ParseResult.GetValueForOption(pathOption)!,
                Pattern = context.ParseResult.GetValueForOption(patternOption)!,
                RegexPattern = context.ParseResult.GetValueForOption(regexOption),
                ModifiedAfter = context.ParseResult.GetValueForOption(modifiedAfterOption),
                ModifiedBefore = context.ParseResult.GetValueForOption(modifiedBeforeOption),
                CreatedAfter = context.ParseResult.GetValueForOption(createdAfterOption),
                CreatedBefore = context.ParseResult.GetValueForOption(createdBeforeOption),
                LargerThan = context.ParseResult.GetValueForOption(largerThanOption),
                SmallerThan = context.ParseResult.GetValueForOption(smallerThanOption),
                MaxResults = context.ParseResult.GetValueForOption(maxResultsOption),
                IncludeHidden = context.ParseResult.GetValueForOption(includeHiddenOption),
                CaseSensitive = context.ParseResult.GetValueForOption(caseSensitiveOption),
                MaxDepth = context.ParseResult.GetValueForOption(maxDepthOption),
                Strategy = context.ParseResult.GetValueForOption(strategyOption),
                HandlerType = context.ParseResult.GetValueForOption(handlerOption),
                Format = context.ParseResult.GetValueForOption(exportOption),
                OutputPath = context.ParseResult.GetValueForOption(outputOption),
                Verbose = context.ParseResult.GetValueForOption(verboseOption)
            };

            try
            {
                var result = await searchService.SearchAsync(searchOptions, context.GetCancellationToken());

                if (result.IsSuccess)
                {
                    var handler = result.Value;
                    
                    var exportResult = await SearchExporter.ExportAsync(
                        handler, 
                        searchOptions.Format, 
                        sizeParser,
                        searchOptions.OutputPath);

                    if (exportResult != null)
                    {
                        Console.Write(exportResult);
                    }
                    else
                    {
                        Console.WriteLine($"Results exported to: {searchOptions.OutputPath}");
                    }

                    if (searchOptions.Verbose && handler.ErrorCount > 0)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Errors encountered:");
                        foreach (var error in handler.Errors.Take(10))
                        {
                            Console.WriteLine($"  {error}");
                        }
                        if (handler.Errors.Count > 10)
                        {
                            Console.WriteLine($"  ... and {handler.Errors.Count - 10} more errors");
                        }
                    }

                    context.ExitCode = 0;
                }
                else
                {
                    logger.LogError("Search failed: {Error}", result.Error.Message);
                    Console.WriteLine($"Search failed: {result.Error.Message}");
                    context.ExitCode = 1;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error during search");
                Console.WriteLine($"Unexpected error: {ex.Message}");
                context.ExitCode = 1;
            }
        });

        var parser = new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .Build();

        return await parser.InvokeAsync(args);
    }
}
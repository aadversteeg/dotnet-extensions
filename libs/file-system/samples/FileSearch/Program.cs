using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Threading.Tasks;
using FileSystemSamples.Common.Options;
using FileSystemSamples.Common.Services;
using FileSearch.Models;
using FileSearch.Services;
using FileSearch.Export;
using FileSearch.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Mapping;

namespace FileSearch;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Search for files with various criteria");

        // Common options
        var pathOption = CommonOptions.CreatePathOption();
        var logLevelOption = CommonOptions.CreateLogLevelOption();
        var verboseOption = CommonOptions.CreateVerboseOption();
        var outputOption = CommonOptions.CreateOutputOption();
        var formatOption = CommonOptions.CreateFormatOption();
        var strategyOption = CommonOptions.CreateStrategyOption();
        var maxDepthOption = CommonOptions.CreateMaxDepthOption();

        // FileSearch specific options
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
            description: "Files larger than this size (e.g., 1MB, 500KB)");

        var smallerThanOption = new Option<string?>(
            aliases: new[] { "--smaller-than" },
            description: "Files smaller than this size (e.g., 1MB, 500KB)");

        var maxResultsOption = new Option<int>(
            aliases: new[] { "--max-results", "-m" },
            description: "Maximum number of results to return",
            getDefaultValue: () => 100);

        var includeHiddenOption = new Option<bool>(
            aliases: new[] { "--include-hidden" },
            description: "Include hidden files in search",
            getDefaultValue: () => false);

        var caseSensitiveOption = new Option<bool>(
            aliases: new[] { "--case-sensitive" },
            description: "Case sensitive pattern matching",
            getDefaultValue: () => false);

        var handlerOption = new Option<HandlerType>(
            aliases: new[] { "--handler", "-h" },
            description: "Handler type: Simple, Detailed, Metadata",
            getDefaultValue: () => HandlerType.Simple);

        // Add all options
        rootCommand.AddOption(pathOption);
        rootCommand.AddOption(logLevelOption);
        rootCommand.AddOption(verboseOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(formatOption);
        rootCommand.AddOption(strategyOption);
        rootCommand.AddOption(maxDepthOption);
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
        rootCommand.AddOption(handlerOption);

        rootCommand.SetHandler(async (context) =>
        {
            var options = new FileSearchOptions
            {
                Path = context.ParseResult.GetValueForOption(pathOption)!,
                LogLevel = context.ParseResult.GetValueForOption(logLevelOption),
                Verbose = context.ParseResult.GetValueForOption(verboseOption),
                OutputPath = context.ParseResult.GetValueForOption(outputOption),
                ExportFormat = context.ParseResult.GetValueForOption(formatOption),
                Strategy = context.ParseResult.GetValueForOption(strategyOption),
                MaxDepth = context.ParseResult.GetValueForOption(maxDepthOption),
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
                HandlerType = context.ParseResult.GetValueForOption(handlerOption)
            };

            // Use common host builder
            var host = HostBuilderFactory.CreateHost(options);
            
            // Add FileSearch specific services
            var serviceProvider = new ServiceCollection()
                .AddSingleton(host.Services.GetRequiredService<IFileSystem>())
                .AddSingleton(host.Services.GetRequiredService<IPathMapper>())
                .AddSingleton(host.Services.GetRequiredService<ILogger<FileSearchService>>())
                .AddSingleton<HandlerFactory>()
                .AddScoped<FileSearchService>()
                .AddSingleton<SizeParser>()
                .AddSingleton<Services.PatternMatcher>()
                .BuildServiceProvider();

            var searchService = serviceProvider.GetRequiredService<FileSearchService>();
            var sizeParser = serviceProvider.GetRequiredService<SizeParser>();
            var logger = host.Services.GetRequiredService<ILogger<Program>>();

            try
            {
                var result = await searchService.SearchAsync(options, context.GetCancellationToken());
                if (result.IsSuccess)
                {
                    var content = await SearchExporter.ExportAsync(
                        result.Value,
                        options.ExportFormat,
                        sizeParser,
                        options.OutputPath);

                    if (options.OutputPath == null)
                    {
                        Console.WriteLine(content);
                    }
                    else
                    {
                        logger.LogInformation("Results exported to: {OutputPath}", options.OutputPath);
                    }

                    context.ExitCode = 0;
                }
                else
                {
                    logger.LogError("Search failed: {Error}", result.Error.Message);
                    context.ExitCode = 1;
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Search was cancelled");
                context.ExitCode = 1;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error occurred");
                context.ExitCode = 1;
            }
        });

        var parser = new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .Build();

        return await parser.InvokeAsync(args);
    }
}
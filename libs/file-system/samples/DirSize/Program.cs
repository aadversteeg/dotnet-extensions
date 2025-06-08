using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Parsing;
using System.IO;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Traversal;
using DirSize.Models;
using DirSize.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;

namespace DirSize;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Calculate directory sizes recursively");

        // Path option
        var pathOption = new Option<string>(
            aliases: new[] { "--path", "-p" },
            description: "Path to analyze",
            getDefaultValue: () => Directory.GetCurrentDirectory());

        // Display options
        var topOption = new Option<int>(
            aliases: new[] { "--top", "-t" },
            description: "Show top N largest directories",
            getDefaultValue: () => 10);

        var minSizeOption = new Option<string?>(
            aliases: new[] { "--min-size", "-m" },
            description: "Minimum size to display (e.g., 1MB, 100KB)");

        var depthOption = new Option<int?>(
            aliases: new[] { "--max-depth", "-d" },
            description: "Maximum depth to analyze (null = unlimited)");

        var sortOption = new Option<SortBy>(
            aliases: new[] { "--sort", "-s" },
            description: "Sort by: Size, Name, FileCount",
            getDefaultValue: () => SortBy.Size);

        // Traversal options
        var strategyOption = new Option<TraversalStrategyHint>(
            aliases: new[] { "--strategy" },
            description: "Traversal strategy: Auto, Search, FullScan, MemoryEfficient, Parallel",
            getDefaultValue: () => TraversalStrategyHint.Auto);

        // Handler options
        var handlerOption = new Option<HandlerType>(
            aliases: new[] { "--handler", "-h" },
            description: "Handler type: Simple, Detailed, Tree",
            getDefaultValue: () => HandlerType.Simple);

        // Export options
        var exportOption = new Option<ExportFormat>(
            aliases: new[] { "--format" },
            description: "Output format: Text, Json, Csv, Xml, Markdown",
            getDefaultValue: () => ExportFormat.Text);

        var outputOption = new Option<string?>(
            aliases: new[] { "--output", "-o" },
            description: "Output file path (uses stdout if not specified)");

        // Logging options
        var verboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "Show detailed error information");

        var logLevelOption = new Option<LogLevel>(
            aliases: new[] { "--log-level", "--loglevel" },
            description: "Logging level: Trace, Debug, Information, Warning, Error, Critical",
            getDefaultValue: () => LogLevel.Warning);

        // Display control
        var showPercentageOption = new Option<bool>(
            aliases: new[] { "--show-percentage" },
            description: "Show percentage of total size for each directory",
            getDefaultValue: () => true);

        var includeHiddenOption = new Option<bool>(
            aliases: new[] { "--include-hidden" },
            description: "Include hidden directories in analysis",
            getDefaultValue: () => false);

        // Add options to command
        rootCommand.AddOption(pathOption);
        rootCommand.AddOption(topOption);
        rootCommand.AddOption(minSizeOption);
        rootCommand.AddOption(depthOption);
        rootCommand.AddOption(sortOption);
        rootCommand.AddOption(strategyOption);
        rootCommand.AddOption(handlerOption);
        rootCommand.AddOption(exportOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(verboseOption);
        rootCommand.AddOption(logLevelOption);
        rootCommand.AddOption(showPercentageOption);
        rootCommand.AddOption(includeHiddenOption);

        // Set handler
        rootCommand.SetHandler(async (context) =>
        {
            // Get log level from command line first
            var logLevel = context.ParseResult.GetValueForOption(logLevelOption);
            
            // Build host with correct log level
            var hostBuilder = Host.CreateDefaultBuilder()
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
                    services.AddScoped<DirSizeService>();
                });

            var host = hostBuilder.Build();
            var service = host.Services.GetRequiredService<DirSizeService>();
            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            
            var path = context.ParseResult.GetValueForOption(pathOption)!;
            var top = context.ParseResult.GetValueForOption(topOption);
            var minSizeStr = context.ParseResult.GetValueForOption(minSizeOption);
            var depth = context.ParseResult.GetValueForOption(depthOption);
            var sortBy = context.ParseResult.GetValueForOption(sortOption);
            var strategy = context.ParseResult.GetValueForOption(strategyOption);
            var handlerType = context.ParseResult.GetValueForOption(handlerOption);
            var format = context.ParseResult.GetValueForOption(exportOption);
            var output = context.ParseResult.GetValueForOption(outputOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);
            var showPercentage = context.ParseResult.GetValueForOption(showPercentageOption);
            var includeHidden = context.ParseResult.GetValueForOption(includeHiddenOption);

            var options = new SizeOptions
            {
                Path = path,
                Top = top,
                MinSizeBytes = SizeParser.Parse(minSizeStr),
                MaxDepth = depth,
                SortBy = sortBy,
                Strategy = strategy,
                HandlerType = handlerType,
                ExportFormat = format,
                OutputPath = output,
                Verbose = verbose,
                LogLevel = logLevel,
                ShowPercentage = showPercentage,
                IncludeHidden = includeHidden
            };

            context.ExitCode = await service.ExecuteAsync(options, context.GetCancellationToken());
        });

        var parser = new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .Build();

        return await parser.InvokeAsync(args);
    }
}
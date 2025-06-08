using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Parsing;
using System.IO;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Traversal;
using FileCounter.Export;
using FileCounter.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FileCounter
{
    /// <summary>
    /// FileCounter - A cross-platform console application for counting files and directories.
    /// Demonstrates usage of Ave.Extensions.FileSystem with various filtering and export options.
    /// </summary>
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand("Count files and directories with filtering and export options");

            // Path option
            var pathOption = new Option<string>(
                aliases: new[] { "--path", "-p" },
                description: "Path to count files in",
                getDefaultValue: () => Directory.GetCurrentDirectory());

            // Filter options
            var extensionOption = new Option<string?>(
                aliases: new[] { "--extension", "-e" },
                description: "Filter by file extension (e.g., .cs, .txt)");

            var minSizeOption = new Option<long>(
                aliases: new[] { "--min-size" },
                description: "Minimum file size in bytes",
                getDefaultValue: () => 0);

            var maxSizeOption = new Option<long>(
                aliases: new[] { "--max-size" },
                description: "Maximum file size in bytes",
                getDefaultValue: () => long.MaxValue);

            // Traversal options
            var recursiveOption = new Option<bool>(
                aliases: new[] { "--recursive", "-r" },
                description: "Count recursively",
                getDefaultValue: () => true);

            var strategyOption = new Option<TraversalStrategyHint>(
                aliases: new[] { "--strategy", "-s" },
                description: "Traversal strategy: Auto, Search, FullScan, MemoryEfficient, Parallel",
                getDefaultValue: () => TraversalStrategyHint.Auto);

            // Handler options
            var handlerOption = new Option<HandlerType>(
                aliases: new[] { "--handler", "-h" },
                description: "Handler type: Simple, Detailed, Summary",
                getDefaultValue: () => HandlerType.Simple);

            // Export options
            var exportOption = new Option<ExportFormat>(
                aliases: new[] { "--format" },
                description: "Output format: Text, Json, Csv, Xml, Markdown",
                getDefaultValue: () => ExportFormat.Text);

            var outputOption = new Option<string?>(
                aliases: new[] { "--output", "-o" },
                description: "Output file path (uses stdout if not specified)");

            var verboseOption = new Option<bool>(
                aliases: new[] { "--verbose", "-v" },
                description: "Show detailed error information",
                getDefaultValue: () => false);

            var maxExtensionsOption = new Option<int>(
                aliases: new[] { "--max-extensions" },
                description: "Maximum number of extensions to show in text format (0 = show all)",
                getDefaultValue: () => 20);

            var logLevelOption = new Option<LogLevel>(
                aliases: new[] { "--log-level", "--loglevel" },
                description: "Logging level: Trace, Debug, Information, Warning, Error, Critical",
                getDefaultValue: () => LogLevel.Warning);

            var maxDepthOption = new Option<int?>(
                aliases: new[] { "--max-depth" },
                description: "Maximum traversal depth (null = unlimited)",
                getDefaultValue: () => null);

            // Add options to command
            rootCommand.AddOption(pathOption);
            rootCommand.AddOption(extensionOption);
            rootCommand.AddOption(minSizeOption);
            rootCommand.AddOption(maxSizeOption);
            rootCommand.AddOption(recursiveOption);
            rootCommand.AddOption(strategyOption);
            rootCommand.AddOption(handlerOption);
            rootCommand.AddOption(exportOption);
            rootCommand.AddOption(outputOption);
            rootCommand.AddOption(verboseOption);
            rootCommand.AddOption(maxExtensionsOption);
            rootCommand.AddOption(logLevelOption);
            rootCommand.AddOption(maxDepthOption);

            // Configure host  
            var builder = Host.CreateDefaultBuilder(args)
                .ConfigureLogging((context, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConsole(options =>
                    {
                        // Send log messages to stderr to keep stdout clean for results
                        options.LogToStandardErrorThreshold = LogLevel.Trace;
                    });
                    // Set default to Warning, will be updated in handler
                    logging.SetMinimumLevel(LogLevel.Warning);
                })
                .ConfigureServices((context, services) =>
                {
                    // Register file system services
                    services.AddFileSystem();
                    
                    // Register application services
                    services.AddSingleton<HandlerFactory>();
                    services.AddScoped<FileCounterService>();
                });

            // Set command handler
            rootCommand.SetHandler(async (context) =>
            {
                // Get log level from command line first
                var logLevel = context.ParseResult.GetValueForOption(logLevelOption);
                
                // Rebuild host with correct log level
                var hostBuilder = Host.CreateDefaultBuilder()
                    .ConfigureLogging((hostContext, logging) =>
                    {
                        logging.ClearProviders();
                        logging.AddConsole(options =>
                        {
                            options.LogToStandardErrorThreshold = LogLevel.Trace;
                        });
                        logging.SetMinimumLevel(logLevel);
                        logging.AddFilter("Microsoft", LogLevel.Warning); // Keep Microsoft logs quieter
                    })
                    .ConfigureServices((hostContext, services) =>
                    {
                        services.AddFileSystem();
                        services.AddSingleton<HandlerFactory>();
                        services.AddScoped<FileCounterService>();
                    });

                var host = hostBuilder.Build();
                var service = host.Services.GetRequiredService<FileCounterService>();
                var logger = host.Services.GetRequiredService<ILogger<Program>>();

                try
                {
                    // Extract options from command line
                    var options = new CountingOptions
                    {
                        Path = context.ParseResult.GetValueForOption(pathOption)!,
                        ExtensionFilter = context.ParseResult.GetValueForOption(extensionOption),
                        MinSize = context.ParseResult.GetValueForOption(minSizeOption),
                        MaxSize = context.ParseResult.GetValueForOption(maxSizeOption),
                        Recursive = context.ParseResult.GetValueForOption(recursiveOption),
                        Strategy = context.ParseResult.GetValueForOption(strategyOption),
                        HandlerType = context.ParseResult.GetValueForOption(handlerOption),
                        ExportFormat = context.ParseResult.GetValueForOption(exportOption),
                        OutputPath = context.ParseResult.GetValueForOption(outputOption),
                        Verbose = context.ParseResult.GetValueForOption(verboseOption),
                        MaxExtensions = context.ParseResult.GetValueForOption(maxExtensionsOption),
                        MaxDepth = context.ParseResult.GetValueForOption(maxDepthOption)
                    };

                    // Validate path exists
                    if (!Directory.Exists(options.Path) && !File.Exists(options.Path))
                    {
                        logger.LogError("Path does not exist: {Path}", options.Path);
                        context.ExitCode = 1;
                        return;
                    }

                    // Execute counting operation
                    var result = await service.CountAndExportAsync(options, context.GetCancellationToken());

                    if (result.IsSuccess)
                    {
                        // Output results to stdout (only if not written to file)
                        if (options.OutputPath == null)
                        {
                            Console.WriteLine(result.Value);
                        }
                        else
                        {
                            logger.LogInformation("Results exported to: {OutputPath}", options.OutputPath);
                        }

                        context.ExitCode = 0;
                    }
                    else
                    {
                        logger.LogError("Operation failed: {Error}", result.Error.Message);
                        context.ExitCode = 1;
                    }
                }
                catch (OperationCanceledException)
                {
                    logger.LogInformation("Operation was cancelled");
                    context.ExitCode = 1;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unexpected error occurred");
                    context.ExitCode = 1;
                }
            });

            // Build and run parser
            var parser = new CommandLineBuilder(rootCommand)
                .UseDefaults()
                .UseExceptionHandler((ex, context) =>
                {
                    Console.Error.WriteLine($"Unhandled exception: {ex.Message}");
                    context.ExitCode = 1;
                })
                .Build();

            return await parser.InvokeAsync(args);
        }
    }
}
using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Threading.Tasks;
using FileSystemSamples.Common.Options;
using FileSystemSamples.Common.Services;
using FileSystemSamples.Common.Export;
using FileSystemSamples.Common.Export.Formatters;
using FileCounter.Models;
using FileCounter.Services;
using FileCounter.Handlers;
using FileCounter.Export;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Mapping;

namespace FileCounter;

/// <summary>
/// FileCounter - A cross-platform console application for counting files and directories.
/// Demonstrates usage of Ave.Extensions.FileSystem with various filtering and export options.
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Count files and directories with filtering and export options");

        // Common options
        var pathOption = CommonOptions.CreatePathOption();
        var logLevelOption = CommonOptions.CreateLogLevelOption();
        var verboseOption = CommonOptions.CreateVerboseOption();
        var outputOption = CommonOptions.CreateOutputOption();
        var formatOption = CommonOptions.CreateFormatOption();
        var strategyOption = CommonOptions.CreateStrategyOption();
        var maxDepthOption = CommonOptions.CreateMaxDepthOption();

        // FileCounter specific options
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

        var recursiveOption = new Option<bool>(
            aliases: new[] { "--recursive", "-r" },
            description: "Count recursively",
            getDefaultValue: () => true);

        var handlerOption = new Option<HandlerType>(
            aliases: new[] { "--handler" },
            description: "Handler type: Simple, Detailed, Summary",
            getDefaultValue: () => HandlerType.Simple);

        var maxExtensionsOption = new Option<int>(
            aliases: new[] { "--max-extensions" },
            description: "Maximum number of extensions to show in text format (0 = show all)",
            getDefaultValue: () => 20);

        // Add all options
        rootCommand.AddOption(pathOption);
        rootCommand.AddOption(logLevelOption);
        rootCommand.AddOption(verboseOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(formatOption);
        rootCommand.AddOption(strategyOption);
        rootCommand.AddOption(maxDepthOption);
        rootCommand.AddOption(extensionOption);
        rootCommand.AddOption(minSizeOption);
        rootCommand.AddOption(maxSizeOption);
        rootCommand.AddOption(recursiveOption);
        rootCommand.AddOption(handlerOption);
        rootCommand.AddOption(maxExtensionsOption);

        rootCommand.SetHandler(async (context) =>
        {
            var options = new FileCounterOptions
            {
                Path = context.ParseResult.GetValueForOption(pathOption)!,
                LogLevel = context.ParseResult.GetValueForOption(logLevelOption),
                Verbose = context.ParseResult.GetValueForOption(verboseOption),
                OutputPath = context.ParseResult.GetValueForOption(outputOption),
                ExportFormat = context.ParseResult.GetValueForOption(formatOption),
                Strategy = context.ParseResult.GetValueForOption(strategyOption),
                MaxDepth = context.ParseResult.GetValueForOption(maxDepthOption),
                ExtensionFilter = context.ParseResult.GetValueForOption(extensionOption),
                MinSize = context.ParseResult.GetValueForOption(minSizeOption),
                MaxSize = context.ParseResult.GetValueForOption(maxSizeOption),
                Recursive = context.ParseResult.GetValueForOption(recursiveOption),
                HandlerType = context.ParseResult.GetValueForOption(handlerOption),
                MaxExtensions = context.ParseResult.GetValueForOption(maxExtensionsOption)
            };

            // Use common host builder
            var host = HostBuilderFactory.CreateHost(options);
            
            // Add FileCounter specific services
            var serviceProvider = new ServiceCollection()
                .AddSingleton(host.Services.GetRequiredService<IFileSystem>())
                .AddSingleton(host.Services.GetRequiredService<IPathMapper>())
                .AddSingleton(host.Services.GetRequiredService<ILogger<FileCounterService>>())
                .AddSingleton(host.Services.GetRequiredService<ILoggerFactory>())
                .AddSingleton(host.Services.GetRequiredService<ILogger<HandlerFactory>>())
                .AddSingleton<HandlerFactory>()
                .AddScoped<FileCounterService>()
                .BuildServiceProvider();

            var service = serviceProvider.GetRequiredService<FileCounterService>();
            var logger = host.Services.GetRequiredService<ILogger<Program>>();

            try
            {
                var result = await service.CountAsync(options, context.GetCancellationToken());
                if (result.IsSuccess)
                {
                    // Handle export if OutputPath is specified
                    if (options.OutputPath != null)
                    {
                        var exporter = new ExporterFactory<ICountingHandler>(format => format switch
                        {
                            ExportFormat.Text => new CountingResultTextFormatter(options.MaxExtensions),
                            ExportFormat.Json => new JsonFormatter<ICountingHandler>(),
                            ExportFormat.Csv => new CountingResultCsvFormatter(),
                            _ => throw new NotSupportedException($"Format {format} not supported")
                        });

                        var exportResult = await exporter.ExportAsync(result.Value, options.ExportFormat, options.OutputPath);
                        if (exportResult == null)
                        {
                            logger.LogInformation("Results exported to: {OutputPath}", options.OutputPath);
                        }
                        else
                        {
                            Console.WriteLine(exportResult);
                        }
                    }
                    else
                    {
                        // Display results to console
                        Console.WriteLine(result.Value.GetSummary());
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

        var parser = new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .Build();

        return await parser.InvokeAsync(args);
    }
}
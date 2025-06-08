using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading.Tasks;
using FileSystemSamples.Common.Options;
using FileSystemSamples.Common.Services;
using ContentTypeAnalyzer.Models;
using ContentTypeAnalyzer.Services;
using ContentTypeAnalyzer.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Mapping;

namespace ContentTypeAnalyzer;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("ContentTypeAnalyzer - Analyze files by their actual content type");

        // Common options
        var pathOption = CommonOptions.CreatePathOption();
        var logLevelOption = CommonOptions.CreateLogLevelOption();
        var verboseOption = CommonOptions.CreateVerboseOption();
        var outputOption = CommonOptions.CreateOutputOption();
        var formatOption = CommonOptions.CreateFormatOption();
        var strategyOption = CommonOptions.CreateStrategyOption();
        var maxDepthOption = CommonOptions.CreateMaxDepthOption();

        // ContentTypeAnalyzer specific options
        var handlerOption = new Option<HandlerType>(
            aliases: new[] { "--handler", "-h" },
            description: "Handler type: Simple, Misnamed, Organize, Detailed",
            getDefaultValue: () => HandlerType.Simple);

        var categoryOption = new Option<FileCategory?>(
            aliases: new[] { "--category", "-c" },
            description: "Filter by category: Image, Document, Archive, Audio, Video, Text, Application, Data");

        var confidenceOption = new Option<double>(
            aliases: new[] { "--confidence" },
            description: "Minimum confidence level 0.0-1.0",
            getDefaultValue: () => 0.7);

        var definitionsOption = new Option<string>(
            aliases: new[] { "--definitions", "-d" },
            description: "Definition pack: default, condensed, exhaustive",
            getDefaultValue: () => "default");

        var usageOption = new Option<string>(
            aliases: new[] { "--usage" },
            description: "Usage type: personal, commercial",
            getDefaultValue: () => "personal");

        var maxBytesOption = new Option<int>(
            aliases: new[] { "--max-bytes" },
            description: "Maximum bytes to read for detection",
            getDefaultValue: () => 8192);

        var fallbackExtensionOption = new Option<bool>(
            aliases: new[] { "--fallback-extension" },
            description: "Use extension if content detection fails",
            getDefaultValue: () => true);

        // Add all options
        rootCommand.AddOption(pathOption);
        rootCommand.AddOption(logLevelOption);
        rootCommand.AddOption(verboseOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(formatOption);
        rootCommand.AddOption(strategyOption);
        rootCommand.AddOption(maxDepthOption);
        rootCommand.AddOption(handlerOption);
        rootCommand.AddOption(categoryOption);
        rootCommand.AddOption(confidenceOption);
        rootCommand.AddOption(definitionsOption);
        rootCommand.AddOption(usageOption);
        rootCommand.AddOption(maxBytesOption);
        rootCommand.AddOption(fallbackExtensionOption);

        rootCommand.SetHandler(async (context) =>
        {
            var options = new ContentAnalyzerOptions
            {
                Path = context.ParseResult.GetValueForOption(pathOption)!,
                LogLevel = context.ParseResult.GetValueForOption(logLevelOption),
                Verbose = context.ParseResult.GetValueForOption(verboseOption),
                OutputPath = context.ParseResult.GetValueForOption(outputOption),
                ExportFormat = context.ParseResult.GetValueForOption(formatOption),
                Strategy = context.ParseResult.GetValueForOption(strategyOption),
                MaxDepth = context.ParseResult.GetValueForOption(maxDepthOption),
                HandlerType = context.ParseResult.GetValueForOption(handlerOption),
                FilterCategory = context.ParseResult.GetValueForOption(categoryOption),
                MinimumConfidence = context.ParseResult.GetValueForOption(confidenceOption),
                DefinitionPack = context.ParseResult.GetValueForOption(definitionsOption)!,
                UsageType = context.ParseResult.GetValueForOption(usageOption)!,
                MaxBytes = context.ParseResult.GetValueForOption(maxBytesOption),
                FallbackExtension = context.ParseResult.GetValueForOption(fallbackExtensionOption)
            };

            // Use common host builder
            var host = HostBuilderFactory.CreateHost(options);
            
            // Add ContentTypeAnalyzer specific services
            var serviceProvider = new ServiceCollection()
                .AddSingleton(host.Services.GetRequiredService<IFileSystem>())
                .AddSingleton(host.Services.GetRequiredService<IPathMapper>())
                .AddSingleton(host.Services.GetRequiredService<ILogger<ContentAnalyzerService>>())
                .AddSingleton<HandlerFactory>()
                .AddScoped<ContentAnalyzerService>()
                .BuildServiceProvider();

            var analyzerService = serviceProvider.GetRequiredService<ContentAnalyzerService>();
            var logger = host.Services.GetRequiredService<ILogger<Program>>();

            try
            {
                var result = await analyzerService.AnalyzeAsync(options, context.GetCancellationToken());
                
                if (result.IsSuccess)
                {
                    DisplayResults(result.Value, options);
                    
                    // Handle export if OutputPath is specified
                    if (options.OutputPath != null && result.Value is DetailedAnalysisHandler detailedHandler)
                    {
                        await detailedHandler.ExportAsync(options.OutputPath, options.ExportFormat);
                        logger.LogInformation("Results exported to: {OutputPath}", options.OutputPath);
                    }
                    
                    context.ExitCode = 0;
                }
                else
                {
                    logger.LogError("Analysis failed: {Error}", result.Error.Message);
                    context.ExitCode = 1;
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Analysis was cancelled");
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

    static void DisplayResults(IContentTypeHandler handler, ContentAnalyzerOptions options)
    {
        Console.WriteLine("Content Type Analysis Results");
        Console.WriteLine("============================");
        Console.WriteLine();
        Console.WriteLine($"Handler: {handler.GetType().Name}");
        Console.WriteLine("Analysis completed successfully!");
        Console.WriteLine();
        Console.WriteLine("Note: Detailed results display will be implemented based on handler-specific properties.");
    }
}
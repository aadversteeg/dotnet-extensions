using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Threading.Tasks;
using FileSystemSamples.Common.Options;
using FileSystemSamples.Common.Services;
using DirSize.Models;
using DirSize.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Mapping;

namespace DirSize;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Calculate directory sizes recursively");

        // Common options
        var pathOption = CommonOptions.CreatePathOption();
        var logLevelOption = CommonOptions.CreateLogLevelOption();
        var verboseOption = CommonOptions.CreateVerboseOption();
        var outputOption = CommonOptions.CreateOutputOption();
        var formatOption = CommonOptions.CreateFormatOption();
        var strategyOption = CommonOptions.CreateStrategyOption();
        var maxDepthOption = CommonOptions.CreateMaxDepthOption();

        // DirSize specific options
        var topOption = new Option<int>(
            aliases: new[] { "--top", "-t" },
            description: "Show top N largest directories",
            getDefaultValue: () => 10);

        var minSizeOption = new Option<string?>(
            aliases: new[] { "--min-size", "-m" },
            description: "Minimum size to display (e.g., 1MB, 100KB)");

        var sortOption = new Option<SortBy>(
            aliases: new[] { "--sort" },
            description: "Sort by: Size, Name, FileCount",
            getDefaultValue: () => SortBy.Size);

        var handlerOption = new Option<HandlerType>(
            aliases: new[] { "--handler", "-h" },
            description: "Handler type: Simple, Detailed, Tree",
            getDefaultValue: () => HandlerType.Simple);

        var showPercentageOption = new Option<bool>(
            aliases: new[] { "--show-percentage" },
            description: "Show percentage of total size for each directory",
            getDefaultValue: () => true);

        var includeHiddenOption = new Option<bool>(
            aliases: new[] { "--include-hidden" },
            description: "Include hidden directories in analysis",
            getDefaultValue: () => false);

        // Add all options
        rootCommand.AddOption(pathOption);
        rootCommand.AddOption(logLevelOption);
        rootCommand.AddOption(verboseOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(formatOption);
        rootCommand.AddOption(strategyOption);
        rootCommand.AddOption(maxDepthOption);
        rootCommand.AddOption(topOption);
        rootCommand.AddOption(minSizeOption);
        rootCommand.AddOption(sortOption);
        rootCommand.AddOption(handlerOption);
        rootCommand.AddOption(showPercentageOption);
        rootCommand.AddOption(includeHiddenOption);

        rootCommand.SetHandler(async (context) =>
        {
            var options = new DirSizeOptions
            {
                Path = context.ParseResult.GetValueForOption(pathOption)!,
                LogLevel = context.ParseResult.GetValueForOption(logLevelOption),
                Verbose = context.ParseResult.GetValueForOption(verboseOption),
                OutputPath = context.ParseResult.GetValueForOption(outputOption),
                ExportFormat = context.ParseResult.GetValueForOption(formatOption),
                Strategy = context.ParseResult.GetValueForOption(strategyOption),
                MaxDepth = context.ParseResult.GetValueForOption(maxDepthOption),
                Top = context.ParseResult.GetValueForOption(topOption),
                MinSizeBytes = SizeParser.Parse(context.ParseResult.GetValueForOption(minSizeOption)),
                SortBy = context.ParseResult.GetValueForOption(sortOption),
                HandlerType = context.ParseResult.GetValueForOption(handlerOption),
                ShowPercentage = context.ParseResult.GetValueForOption(showPercentageOption),
                IncludeHidden = context.ParseResult.GetValueForOption(includeHiddenOption)
            };

            // Use common host builder
            var host = HostBuilderFactory.CreateHost(options);
            
            // Add DirSize specific services
            var serviceProvider = new ServiceCollection()
                .AddSingleton(host.Services.GetRequiredService<IFileSystem>())
                .AddSingleton(host.Services.GetRequiredService<IPathMapper>())
                .AddSingleton(host.Services.GetRequiredService<ILogger<DirSizeService>>())
                .AddSingleton<HandlerFactory>()
                .AddScoped<DirSizeService>()
                .BuildServiceProvider();

            var service = serviceProvider.GetRequiredService<DirSizeService>();

            context.ExitCode = await service.ExecuteAsync(options, context.GetCancellationToken());
        });

        var parser = new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .Build();

        return await parser.InvokeAsync(args);
    }
}
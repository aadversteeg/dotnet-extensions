using System;
using System.CommandLine;
using System.CommandLine.Hosting;
using System.Linq;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Traversal;
using ContentTypeAnalyzer.Handlers;
using ContentTypeAnalyzer.Models;
using ContentTypeAnalyzer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var pathOption = new Option<string>(
    aliases: new[] { "--path", "-p" },
    description: "Path to analyze",
    getDefaultValue: () => ".");

var handlerOption = new Option<HandlerType>(
    aliases: new[] { "--handler", "-h" },
    description: "Handler type: simple, misnamed, organize, detailed",
    getDefaultValue: () => HandlerType.Simple);

var categoryOption = new Option<FileCategory?>(
    aliases: new[] { "--category", "-c" },
    description: "Filter by category: image, document, archive, audio, video, text, application, data");

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

var strategyOption = new Option<TraversalStrategyHint>(
    aliases: new[] { "--strategy" },
    description: "Traversal strategy: Auto, Search, FullScan, MemoryEfficient, Parallel",
    getDefaultValue: () => TraversalStrategyHint.Auto);

var outputOption = new Option<string?>(
    aliases: new[] { "--output", "-o" },
    description: "Output path for organize handler or export file");

var exportOption = new Option<string?>(
    aliases: new[] { "--export", "-e" },
    description: "Export format: text, json, csv, xml");

var maxBytesOption = new Option<int>(
    aliases: new[] { "--max-bytes" },
    description: "Maximum bytes to read for detection",
    getDefaultValue: () => 8192);

var fallbackExtensionOption = new Option<bool>(
    aliases: new[] { "--fallback-extension" },
    description: "Use extension if content detection fails",
    getDefaultValue: () => true);

var maxDepthOption = new Option<int?>(
    aliases: new[] { "--max-depth" },
    description: "Maximum directory depth to traverse");

var verboseOption = new Option<bool>(
    aliases: new[] { "--verbose", "-v" },
    description: "Enable verbose logging (sets log level to Debug)");

var logLevelOption = new Option<LogLevel>(
    aliases: new[] { "--log-level", "-l" },
    description: "Set logging level: Trace, Debug, Information, Warning, Error, Critical",
    getDefaultValue: () => LogLevel.Warning);

var rootCommand = new RootCommand("ContentTypeAnalyzer - Analyze files by their actual content type")
{
    pathOption,
    handlerOption,
    categoryOption,
    confidenceOption,
    definitionsOption,
    usageOption,
    strategyOption,
    outputOption,
    exportOption,
    maxBytesOption,
    fallbackExtensionOption,
    maxDepthOption,
    verboseOption,
    logLevelOption
};

rootCommand.SetHandler(async (string path, HandlerType handler, bool verbose, LogLevel logLevel) =>
{
    // If verbose is specified, override log level to Debug
    var effectiveLogLevel = verbose ? LogLevel.Debug : logLevel;
    
    var hostBuilder = Host.CreateDefaultBuilder()
        .ConfigureLogging((hostContext, logging) =>
        {
            logging.ClearProviders();
            logging.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            });
            logging.SetMinimumLevel(effectiveLogLevel);
            logging.AddFilter("Microsoft", LogLevel.Warning);
        })
        .ConfigureServices((hostContext, services) =>
        {
            services.AddFileSystem();
            services.AddSingleton<HandlerFactory>();
            services.AddScoped<ContentAnalyzerService>();
        });

    using var host = hostBuilder.Build();
    var analyzerService = host.Services.GetRequiredService<ContentAnalyzerService>();
    
    var options = new AnalysisOptions
    {
        Path = path,
        HandlerType = handler
    };

    try
    {
        var result = await analyzerService.AnalyzeAsync(options);
        
        if (result.IsSuccess)
        {
            DisplayResults(result.Value, options);
        }
        else
        {
            Console.Error.WriteLine($"Error: {result.Error.Message}");
            Environment.Exit(1);
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Unexpected error: {ex.Message}");
        Environment.Exit(1);
    }
}, pathOption, handlerOption, verboseOption, logLevelOption);

await rootCommand.InvokeAsync(args);

static void DisplayResults(IContentTypeHandler handler, AnalysisOptions options)
{
    Console.WriteLine("Content Type Analysis Results");
    Console.WriteLine("============================");
    Console.WriteLine();

    var stats = handler.Statistics;
    
    switch (handler)
    {
        case SimpleContentTypeHandler simple:
            DisplaySimpleResults(stats);
            break;
            
        case MisnamedFilesHandler misnamed:
            DisplayMisnamedResults(misnamed);
            break;
            
        case OrganizeFilesHandler organize:
            DisplayOrganizeResults(organize);
            break;
            
        case DetailedAnalysisHandler detailed:
            DisplayDetailedResults(detailed);
            break;
            
        default:
            Console.WriteLine($"Handler type {handler.GetType().Name} display not implemented");
            break;
    }
}

static void DisplaySimpleResults(AnalysisStatistics stats)
{
    Console.WriteLine("Content Type Summary:");
    foreach (var kvp in stats.ContentTypeCounts.OrderByDescending(x => x.Value))
    {
        Console.WriteLine($"{kvp.Key,-30} {kvp.Value,8} files");
    }
    
    Console.WriteLine();
    Console.WriteLine($"Total: {stats.TotalFiles:N0} files ({FormatBytes(stats.TotalSize)})");
    
    if (stats.CategoryCounts.Any())
    {
        Console.WriteLine();
        Console.WriteLine("Categories:");
        foreach (var kvp in stats.CategoryCounts.OrderByDescending(x => x.Value))
        {
            var percentage = stats.TotalFiles > 0 ? (double)kvp.Value / stats.TotalFiles * 100 : 0;
            Console.WriteLine($"{kvp.Key,-12} {kvp.Value,8} files ({percentage:F1}%)");
        }
    }
    
    if (stats.ConfidenceLevels.Any())
    {
        Console.WriteLine();
        Console.WriteLine("Detection Confidence:");
        foreach (var kvp in stats.ConfidenceLevels)
        {
            Console.WriteLine($"{kvp.Key,-12} {kvp.Value,8} files");
        }
    }
}

static void DisplayMisnamedResults(MisnamedFilesHandler handler)
{
    var mismatched = handler.MismatchedFiles;
    
    Console.WriteLine($"Found {mismatched.Count} potentially misnamed files:");
    Console.WriteLine();
    
    var highRisk = mismatched.Where(f => f.SecurityRisk == "High").ToList();
    var mediumRisk = mismatched.Where(f => f.SecurityRisk == "Medium").ToList();
    var lowRisk = mismatched.Where(f => f.SecurityRisk == "Low").ToList();
    
    if (highRisk.Any())
    {
        Console.WriteLine($"HIGH RISK ({highRisk.Count} files):");
        Console.WriteLine("--------------------");
        foreach (var file in highRisk)
        {
            Console.WriteLine($"{file.Path}");
            Console.WriteLine($"  Actual: {file.DetectedContentType}");
            Console.WriteLine($"  Expected: {string.Join(", ", file.ExpectedExtensions)}");
            Console.WriteLine($"  Confidence: {file.Confidence:F2}");
            Console.WriteLine($"  Risk: Executable content disguised as {file.CurrentExtension}");
            Console.WriteLine();
        }
    }
    
    if (mediumRisk.Any())
    {
        Console.WriteLine($"MEDIUM RISK ({mediumRisk.Count} files):");
        Console.WriteLine("----------------------");
        foreach (var file in mediumRisk)
        {
            Console.WriteLine($"{file.Path}");
            Console.WriteLine($"  Actual: {file.DetectedContentType}");
            Console.WriteLine($"  Expected: {string.Join(", ", file.ExpectedExtensions)}");
            Console.WriteLine($"  Confidence: {file.Confidence:F2}");
            Console.WriteLine();
        }
    }
    
    if (lowRisk.Any())
    {
        Console.WriteLine($"LOW RISK ({lowRisk.Count} files):");
        Console.WriteLine("-------------------");
        foreach (var file in lowRisk.Take(10)) // Show only first 10 to avoid spam
        {
            Console.WriteLine($"{file.Path}");
            Console.WriteLine($"  Actual: {file.DetectedContentType}");
            Console.WriteLine($"  Expected: {string.Join(", ", file.ExpectedExtensions)}");
            Console.WriteLine($"  Confidence: {file.Confidence:F2}");
            Console.WriteLine();
        }
        
        if (lowRisk.Count > 10)
        {
            Console.WriteLine($"... and {lowRisk.Count - 10} more low-risk files");
        }
    }
}

static string FormatBytes(long bytes)
{
    string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
    int counter = 0;
    decimal number = bytes;
    
    while (Math.Round(number / 1024) >= 1)
    {
        number /= 1024;
        counter++;
    }
    
    return $"{number:N1} {suffixes[counter]}";
}

static void DisplayOrganizeResults(OrganizeFilesHandler handler)
{
    var stats = handler.Statistics;
    
    Console.WriteLine("File Organization Analysis");
    Console.WriteLine("==========================");
    Console.WriteLine();
    
    Console.WriteLine($"Total files analyzed: {stats.TotalFiles:N0} ({FormatBytes(stats.TotalSize)})");
    Console.WriteLine();
    
    Console.WriteLine("Files by Category:");
    foreach (var kvp in handler.FilesByCategory.OrderByDescending(x => x.Value.Count))
    {
        Console.WriteLine($"{kvp.Key,-12} {kvp.Value.Count,8} files");
    }
    
    Console.WriteLine();
    Console.WriteLine("To generate organization plan, use the organize command with an output directory.");
    Console.WriteLine("Example: --handler organize --output /organized-files");
}

static void DisplayDetailedResults(DetailedAnalysisHandler handler)
{
    var stats = handler.Statistics;
    
    Console.WriteLine("Detailed Content Analysis");
    Console.WriteLine("========================");
    Console.WriteLine();
    
    Console.WriteLine($"Total files analyzed: {stats.TotalFiles:N0} ({FormatBytes(stats.TotalSize)})");
    Console.WriteLine();
    
    Console.WriteLine("Content Type Distribution:");
    foreach (var kvp in stats.ContentTypeCounts.OrderByDescending(x => x.Value).Take(10))
    {
        Console.WriteLine($"{kvp.Key,-40} {kvp.Value,8} files");
    }
    
    if (stats.ContentTypeCounts.Count > 10)
    {
        Console.WriteLine($"... and {stats.ContentTypeCounts.Count - 10} more content types");
    }
    
    Console.WriteLine();
    Console.WriteLine("Security Assessment Summary:");
    var securityGroups = handler.Analyses
        .GroupBy(a => a.SecurityAssessment)
        .OrderByDescending(g => g.Count())
        .ToList();
        
    foreach (var group in securityGroups)
    {
        Console.WriteLine($"{group.Key,-30} {group.Count(),8} files");
    }
    
    Console.WriteLine();
    Console.WriteLine("To export detailed results, use the --export option.");
    Console.WriteLine("Example: --handler detailed --export json --output analysis.json");
}
using System.CommandLine;
using Microsoft.Extensions.Logging;
using Ave.Extensions.FileSystem.Traversal;

namespace FileSystemSamples.Common.Options;

public static class CommonOptions
{
    public static Option<string> CreatePathOption() =>
        new(new[] { "--path", "-p" },
            description: "Path to process",
            getDefaultValue: () => System.IO.Directory.GetCurrentDirectory());

    public static Option<LogLevel> CreateLogLevelOption() =>
        new(new[] { "--log-level" },
            description: "Logging level: Trace, Debug, Information, Warning, Error, Critical",
            getDefaultValue: () => LogLevel.Warning);

    public static Option<bool> CreateVerboseOption() =>
        new(new[] { "--verbose", "-v" },
            description: "Show detailed information",
            getDefaultValue: () => false);

    public static Option<string?> CreateOutputOption() =>
        new(new[] { "--output", "-o" },
            description: "Output file path (uses stdout if not specified)");

    public static Option<ExportFormat> CreateFormatOption() =>
        new(new[] { "--format", "-f" },
            description: "Output format: Text, Json, Csv, Xml, Markdown",
            getDefaultValue: () => ExportFormat.Text);

    public static Option<TraversalStrategyHint> CreateStrategyOption() =>
        new(new[] { "--strategy", "-s" },
            description: "Traversal strategy: Auto, Search, FullScan, MemoryEfficient, Parallel",
            getDefaultValue: () => TraversalStrategyHint.Auto);

    public static Option<int?> CreateMaxDepthOption() =>
        new(new[] { "--max-depth", "-d" },
            description: "Maximum directory depth to traverse");
}
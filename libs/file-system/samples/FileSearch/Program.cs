using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Parsing;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace FileSearch
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand("Search for files with various criteria");

            // Options
            var pathOption = new Option<string>(
                aliases: new[] { "--path", "-p" },
                description: "Path to search in",
                getDefaultValue: () => Directory.GetCurrentDirectory());

            var patternOption = new Option<string>(
                aliases: new[] { "--pattern", "-n" },
                description: "File name pattern (supports wildcards: *, ?)",
                getDefaultValue: () => "*");

            var regexOption = new Option<string?>(
                aliases: new[] { "--regex", "-r" },
                description: "Regular expression for file names");

            var modifiedAfterOption = new Option<DateTime?>(
                aliases: new[] { "--modified-after", "-ma" },
                description: "Files modified after this date");

            var modifiedBeforeOption = new Option<DateTime?>(
                aliases: new[] { "--modified-before", "-mb" },
                description: "Files modified before this date");

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

            rootCommand.AddOption(pathOption);
            rootCommand.AddOption(patternOption);
            rootCommand.AddOption(regexOption);
            rootCommand.AddOption(modifiedAfterOption);
            rootCommand.AddOption(modifiedBeforeOption);
            rootCommand.AddOption(largerThanOption);
            rootCommand.AddOption(smallerThanOption);
            rootCommand.AddOption(maxResultsOption);
            rootCommand.AddOption(includeHiddenOption);

            // Configure host
            var builder = Host.CreateDefaultBuilder(args)
                .ConfigureLogging((context, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConsole(options =>
                    {
                        options.LogToStandardErrorThreshold = LogLevel.Trace;
                    });
                    logging.SetMinimumLevel(LogLevel.Information);
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<IPathMapper, WindowsPathMapper>();
                    services.AddSingleton<IFileSystem>(sp => 
                        new LocalFileSystem(sp.GetRequiredService<IPathMapper>()));
                    services.AddScoped<FileSearchHandler>();
                });

            // Set handler
            rootCommand.SetHandler(async (context) =>
            {
                var host = context.GetHost();
                var handler = host.Services.GetRequiredService<FileSearchHandler>();
                
                var searchCriteria = new SearchCriteria
                {
                    Path = context.ParseResult.GetValueForOption(pathOption)!,
                    Pattern = context.ParseResult.GetValueForOption(patternOption)!,
                    Regex = context.ParseResult.GetValueForOption(regexOption),
                    ModifiedAfter = context.ParseResult.GetValueForOption(modifiedAfterOption),
                    ModifiedBefore = context.ParseResult.GetValueForOption(modifiedBeforeOption),
                    LargerThan = ParseSize(context.ParseResult.GetValueForOption(largerThanOption)),
                    SmallerThan = ParseSize(context.ParseResult.GetValueForOption(smallerThanOption)),
                    MaxResults = context.ParseResult.GetValueForOption(maxResultsOption),
                    IncludeHidden = context.ParseResult.GetValueForOption(includeHiddenOption)
                };

                await handler.ExecuteAsync(searchCriteria);
            });

            var parser = new CommandLineBuilder(rootCommand)
                .UseHost(builder.Build)
                .UseDefaults()
                .Build();

            return await parser.InvokeAsync(args);
        }

        private static long? ParseSize(string? sizeStr)
        {
            if (string.IsNullOrEmpty(sizeStr))
                return null;

            sizeStr = sizeStr.ToUpper();
            
            var multipliers = new (string suffix, long multiplier)[]
            {
                ("TB", 1024L * 1024 * 1024 * 1024),
                ("GB", 1024L * 1024 * 1024),
                ("MB", 1024 * 1024),
                ("KB", 1024),
                ("B", 1)
            };

            foreach (var (suffix, multiplier) in multipliers)
            {
                if (sizeStr.EndsWith(suffix))
                {
                    var numberStr = sizeStr.Substring(0, sizeStr.Length - suffix.Length);
                    if (double.TryParse(numberStr, out var number))
                    {
                        return (long)(number * multiplier);
                    }
                }
            }

            if (long.TryParse(sizeStr, out var bytes))
            {
                return bytes;
            }

            return null;
        }
    }

    public class SearchCriteria
    {
        public string Path { get; set; } = "";
        public string Pattern { get; set; } = "*";
        public string? Regex { get; set; }
        public DateTime? ModifiedAfter { get; set; }
        public DateTime? ModifiedBefore { get; set; }
        public long? LargerThan { get; set; }
        public long? SmallerThan { get; set; }
        public int MaxResults { get; set; } = 100;
        public bool IncludeHidden { get; set; }
    }

    public class FileSearchHandler
    {
        private readonly IFileSystem _fileSystem;
        private readonly ILogger<FileSearchHandler> _logger;

        public FileSearchHandler(IFileSystem fileSystem, ILogger<FileSearchHandler> logger)
        {
            _fileSystem = fileSystem;
            _logger = logger;
        }

        public async Task ExecuteAsync(SearchCriteria criteria)
        {
            try
            {
                _logger.LogInformation("Searching in: {Path}", criteria.Path);
                _logger.LogDebug("Search criteria: Pattern={Pattern}, Regex={Regex}, ModifiedAfter={ModifiedAfter}, ModifiedBefore={ModifiedBefore}, LargerThan={LargerThan}, SmallerThan={SmallerThan}",
                    criteria.Pattern, criteria.Regex, criteria.ModifiedAfter, criteria.ModifiedBefore, criteria.LargerThan, criteria.SmallerThan);

                // Create canonical path
                var canonicalResult = CanonicalPath.Create(criteria.Path.Replace('\\', '/').StartsWith('/') ? criteria.Path.Replace('\\', '/') : "/" + criteria.Path.Replace('\\', '/'));
                if (canonicalResult.IsFailure)
                {
                    _logger.LogError("Invalid path: {Error}", canonicalResult.Error.Message);
                    return;
                }

                // Create search handler
                var searchHandler = new SearchHandler(criteria);
                var mapper = new DefaultMapper();

                // Configure traversal options
                var options = new TraversalOptions
                {
                    MaxDepth = int.MaxValue,
                    FollowSymlinks = false,
                    StrategyHint = TraversalStrategyHint.Search // Optimized for search
                };

                // Perform traversal
                var result = await _fileSystem.TraverseAsync(
                    canonicalResult.Value,
                    searchHandler,
                    mapper,
                    options);

                if (result.IsSuccess)
                {
                    // Output results to stdout
                    if (searchHandler.FoundFiles.Count == 0)
                    {
                        Console.WriteLine("No files found matching the criteria.");
                    }
                    else
                    {
                        Console.WriteLine($"Found {searchHandler.FoundFiles.Count} file(s):");
                        Console.WriteLine();

                        foreach (var file in searchHandler.FoundFiles)
                        {
                            Console.WriteLine($"{file.Path}");
                            Console.WriteLine($"  Size: {FormatSize(file.Size)}");
                            Console.WriteLine($"  Modified: {file.Modified:yyyy-MM-dd HH:mm:ss}");
                            Console.WriteLine();
                        }

                        if (searchHandler.StoppedEarly)
                        {
                            Console.WriteLine($"Search stopped after finding {criteria.MaxResults} results.");
                        }
                    }
                }
                else
                {
                    _logger.LogError("Search failed: {Error}", result.Error.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during search");
            }
        }

        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    // Custom traversal handler for searching
    public class SearchHandler : ITraversalHandler<FileSystemEntry>
    {
        private readonly SearchCriteria _criteria;
        private readonly Regex? _regex;
        private readonly List<FoundFile> _foundFiles = new();

        public IReadOnlyList<FoundFile> FoundFiles => _foundFiles;
        public bool StoppedEarly { get; private set; }

        public SearchHandler(SearchCriteria criteria)
        {
            _criteria = criteria;
            if (!string.IsNullOrEmpty(criteria.Regex))
            {
                _regex = new Regex(criteria.Regex, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }
        }

        public Task<Result<Unit, FileSystemError>> OnTraversalStartAsync(CanonicalPath rootPath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
        }

        public Task<Result<TraversalControl, FileSystemError>> OnDirectoryEnterAsync(FileSystemEntry directory, TraversalContext context, CancellationToken cancellationToken = default)
        {
            // Skip hidden directories if not included
            if (!_criteria.IncludeHidden && directory is DirectoryEntry dirEntry)
            {
                if ((dirEntry.Info.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
                {
                    return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.SkipSubtree));
                }
            }

            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }

        public Task<Result<TraversalControl, FileSystemError>> OnFileAsync(FileSystemEntry file, TraversalContext context, CancellationToken cancellationToken = default)
        {
            if (_foundFiles.Count >= _criteria.MaxResults)
            {
                StoppedEarly = true;
                return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Stop));
            }

            if (file is FileEntry fileEntry)
            {
                // Check if file matches criteria
                if (MatchesFile(fileEntry))
                {
                    _foundFiles.Add(new FoundFile(
                        fileEntry.Path.Value,
                        fileEntry.Info.Size,
                        fileEntry.Info.LastWriteTimeUtc
                    ));
                }
            }

            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }

        public Task<Result<TraversalControl, FileSystemError>> OnDirectoryAsync(FileSystemEntry directory, TraversalContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }

        public Task<Result<Unit, FileSystemError>> OnTraversalCompleteAsync(TraversalSummary summary, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
        }

        private bool MatchesFile(FileEntry file)
        {
            // Check hidden files
            if (!_criteria.IncludeHidden && (file.Info.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
            {
                return false;
            }

            // Check name pattern or regex
            var fileName = file.Path.GetName();
            if (_regex != null)
            {
                if (!_regex.IsMatch(fileName))
                    return false;
            }
            else if (!MatchesPattern(fileName, _criteria.Pattern))
            {
                return false;
            }

            // Check size constraints
            if (_criteria.LargerThan.HasValue && file.Info.Size <= _criteria.LargerThan.Value)
                return false;

            if (_criteria.SmallerThan.HasValue && file.Info.Size >= _criteria.SmallerThan.Value)
                return false;

            // Check date constraints
            if (_criteria.ModifiedAfter.HasValue && file.Info.LastWriteTimeUtc < _criteria.ModifiedAfter.Value)
                return false;

            if (_criteria.ModifiedBefore.HasValue && file.Info.LastWriteTimeUtc > _criteria.ModifiedBefore.Value)
                return false;

            return true;
        }

        private bool MatchesPattern(string fileName, string pattern)
        {
            // Simple wildcard matching
            var regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            
            return Regex.IsMatch(fileName, regexPattern, RegexOptions.IgnoreCase);
        }

        public record FoundFile(string Path, long Size, DateTime Modified);
    }

    // Simple mapper that returns FileSystemEntry as-is
    public class DefaultMapper : IFileSystemEntryMapper<FileSystemEntry>
    {
        public Task<Result<FileSystemEntry, FileSystemError>> MapFileAsync(FileEntry file, IFileReader fileReader, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<FileSystemEntry, FileSystemError>.Success(file));
        }

        public Task<Result<FileSystemEntry, FileSystemError>> MapDirectoryAsync(DirectoryEntry directory, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<FileSystemEntry, FileSystemError>.Success(directory));
        }
    }

    // Windows path mapper implementation
    public class WindowsPathMapper : IPathMapper
    {
        public Result<string, Error> MapToSystemPath(CanonicalPath canonicalPath)
        {
            var windowsPath = canonicalPath.Value.Replace('/', '\\');
            if (windowsPath.StartsWith("\\") && !windowsPath.StartsWith("\\\\"))
            {
                windowsPath = windowsPath.Substring(1);
            }
            return Result<string, Error>.Success(windowsPath);
        }

        public Result<CanonicalPath, Error> MapToCanonicalPath(string systemPath)
        {
            var canonical = systemPath.Replace('\\', '/');
            if (!canonical.StartsWith('/'))
            {
                canonical = "/" + canonical;
            }
            return CanonicalPath.Create(canonical);
        }
    }
}

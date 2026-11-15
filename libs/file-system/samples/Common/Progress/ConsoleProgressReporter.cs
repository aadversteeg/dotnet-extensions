using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace FileSystemSamples.Common.Progress;

public class ConsoleProgressReporter : IProgressReporter
{
    private readonly ILogger<ConsoleProgressReporter> _logger;
    private readonly bool _verbose;
    private readonly TimeSpan _updateInterval;
    private DateTime _lastUpdate = DateTime.MinValue;
    private readonly Stopwatch _stopwatch = new();
    
    public ConsoleProgressReporter(ILogger<ConsoleProgressReporter> logger, bool verbose = false, TimeSpan? updateInterval = null)
    {
        _logger = logger;
        _verbose = verbose;
        _updateInterval = updateInterval ?? TimeSpan.FromSeconds(1);
        _stopwatch.Start();
    }
    
    public void Report(ProgressInfo progress)
    {
        if (DateTime.UtcNow - _lastUpdate < _updateInterval)
            return;
            
        _lastUpdate = DateTime.UtcNow;
        
        var message = $"Progress: {progress.FilesProcessed:N0} files, {progress.DirectoriesProcessed:N0} directories processed";
        
        if (_verbose && !string.IsNullOrEmpty(progress.CurrentPath))
        {
            message += $" - Current: {TruncatePath(progress.CurrentPath, 50)}";
        }
        
        if (progress.ErrorCount > 0)
        {
            message += $" ({progress.ErrorCount} errors)";
        }
        
        // Use \r to overwrite the same line
        Console.Error.Write($"\r{message,-80}");
    }
    
    public void ReportComplete(CompletionInfo completion)
    {
        // Clear the progress line
        Console.Error.Write($"\r{new string(' ', 80)}\r");
        
        _logger.LogInformation("Operation completed: {TotalFiles:N0} files, {TotalDirectories:N0} directories in {TotalTime}",
            completion.TotalFiles,
            completion.TotalDirectories,
            FormatTimeSpan(completion.TotalTime));
            
        if (completion.TotalErrors > 0)
        {
            _logger.LogWarning("Completed with {ErrorCount} errors", completion.TotalErrors);
        }
        
        if (completion.WasCancelled)
        {
            _logger.LogWarning("Operation was cancelled");
        }
    }
    
    private static string TruncatePath(string path, int maxLength)
    {
        if (path.Length <= maxLength)
            return path;
            
        var fileName = Path.GetFileName(path);
        if (fileName.Length >= maxLength)
            return "..." + fileName.Substring(fileName.Length - maxLength + 3);
            
        var directory = Path.GetDirectoryName(path) ?? "";
        var availableLength = maxLength - fileName.Length - 4; // 4 for "\..."
        
        if (availableLength > 0)
        {
            return directory.Substring(0, Math.Min(directory.Length, availableLength)) + "\\..." + fileName;
        }
        
        return "..." + fileName;
    }
    
    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalSeconds < 1)
            return $"{timeSpan.TotalMilliseconds:F1}ms";
        if (timeSpan.TotalMinutes < 1)
            return $"{timeSpan.TotalSeconds:F1}s";
        if (timeSpan.TotalHours < 1)
            return $"{timeSpan.Minutes}m {timeSpan.Seconds}s";
        return $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m {timeSpan.Seconds}s";
    }
}
using System;

namespace FileSystemSamples.Common.Progress;

public interface IProgressReporter
{
    void Report(ProgressInfo progress);
    void ReportComplete(CompletionInfo completion);
}

public record ProgressInfo(
    int FilesProcessed,
    int DirectoriesProcessed,
    long BytesProcessed,
    TimeSpan Elapsed,
    string? CurrentPath = null,
    int ErrorCount = 0
);

public record CompletionInfo(
    int TotalFiles,
    int TotalDirectories,
    long TotalBytes,
    TimeSpan TotalTime,
    int TotalErrors,
    bool WasCancelled = false
);

public interface IProgressAware
{
    IProgressReporter? ProgressReporter { get; set; }
}
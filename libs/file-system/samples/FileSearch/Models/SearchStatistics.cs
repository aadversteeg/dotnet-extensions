using System;

namespace FileSearch.Models;

public record SearchStatistics
{
    public int FilesScanned { get; init; }
    public int DirectoriesScanned { get; init; }
    public int ErrorCount { get; init; }
    public bool StoppedEarly { get; init; }
    public TimeSpan ElapsedTime { get; init; }
    public int ResultsFound { get; init; }
}
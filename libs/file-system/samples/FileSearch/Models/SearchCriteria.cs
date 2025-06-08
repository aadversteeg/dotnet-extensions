using System;
using System.Text.RegularExpressions;

namespace FileSearch.Models;

public record SearchCriteria
{
    public required string Path { get; init; }
    public string Pattern { get; init; } = "*";
    public string? RegexPattern { get; init; }
    public Regex? CompiledRegex { get; init; }
    public DateTime? ModifiedAfter { get; init; }
    public DateTime? ModifiedBefore { get; init; }
    public DateTime? CreatedAfter { get; init; }
    public DateTime? CreatedBefore { get; init; }
    public long? LargerThan { get; init; }
    public long? SmallerThan { get; init; }
    public int MaxResults { get; init; } = 100;
    public bool IncludeHidden { get; init; }
    public bool CaseSensitive { get; init; }
    public int? MaxDepth { get; init; }
}
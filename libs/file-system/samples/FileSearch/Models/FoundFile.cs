using System;
using System.IO;
using System.Collections.Generic;

namespace FileSearch.Models;

public record FoundFile
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public long Size { get; init; }
    public string? Extension { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public DateTime CreatedUtc { get; init; }
    public FileAttributes Attributes { get; init; }
    public string? Preview { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}
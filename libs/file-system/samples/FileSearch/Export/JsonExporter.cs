using System;
using System.Linq;
using System.Text.Json;
using FileSearch.Handlers;

namespace FileSearch.Export;

public class JsonExporter : IExporter
{
    public string Export(ISearchHandler handler)
    {
        var result = new
        {
            SearchCriteria = new
            {
                handler.Criteria.Path,
                handler.Criteria.Pattern,
                handler.Criteria.RegexPattern,
                handler.Criteria.ModifiedAfter,
                handler.Criteria.ModifiedBefore,
                handler.Criteria.CreatedAfter,
                handler.Criteria.CreatedBefore,
                handler.Criteria.LargerThan,
                handler.Criteria.SmallerThan,
                handler.Criteria.MaxResults,
                handler.Criteria.IncludeHidden,
                handler.Criteria.CaseSensitive,
                handler.Criteria.MaxDepth
            },
            Results = handler.FoundFiles.Select(f => new
            {
                f.Path,
                f.Name,
                f.Size,
                f.Extension,
                f.ModifiedUtc,
                f.CreatedUtc,
                f.Attributes,
                f.Preview,
                f.Metadata
            }),
            Statistics = new
            {
                handler.FilesScanned,
                handler.DirectoriesScanned,
                handler.ErrorCount,
                handler.StoppedEarly,
                ElapsedTimeMs = handler.ElapsedTime.TotalMilliseconds,
                ResultsFound = handler.FoundFiles.Count
            },
            GeneratedAt = DateTime.UtcNow
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Serialize(result, options);
    }
}
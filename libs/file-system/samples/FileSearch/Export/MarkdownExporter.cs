using System;
using System.Text;
using FileSearch.Handlers;
using FileSearch.Services;

namespace FileSearch.Export;

public class MarkdownExporter : IExporter
{
    private readonly SizeParser _sizeParser;

    public MarkdownExporter(SizeParser sizeParser)
    {
        _sizeParser = sizeParser;
    }

    public string Export(ISearchHandler handler)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("# File Search Results");
        sb.AppendLine();

        sb.AppendLine("## Search Criteria");
        sb.AppendLine();
        sb.AppendLine($"- **Path:** `{handler.Criteria.Path}`");
        sb.AppendLine($"- **Pattern:** `{handler.Criteria.Pattern}`");
        if (handler.Criteria.RegexPattern != null)
            sb.AppendLine($"- **Regex:** `{handler.Criteria.RegexPattern}`");
        if (handler.Criteria.ModifiedAfter.HasValue)
            sb.AppendLine($"- **Modified After:** {handler.Criteria.ModifiedAfter:yyyy-MM-dd}");
        if (handler.Criteria.ModifiedBefore.HasValue)
            sb.AppendLine($"- **Modified Before:** {handler.Criteria.ModifiedBefore:yyyy-MM-dd}");
        if (handler.Criteria.LargerThan.HasValue)
            sb.AppendLine($"- **Larger Than:** {_sizeParser.FormatSize(handler.Criteria.LargerThan.Value)}");
        if (handler.Criteria.SmallerThan.HasValue)
            sb.AppendLine($"- **Smaller Than:** {_sizeParser.FormatSize(handler.Criteria.SmallerThan.Value)}");
        sb.AppendLine($"- **Max Results:** {handler.Criteria.MaxResults}");
        sb.AppendLine($"- **Include Hidden:** {handler.Criteria.IncludeHidden}");
        sb.AppendLine($"- **Case Sensitive:** {handler.Criteria.CaseSensitive}");
        sb.AppendLine();

        if (handler.FoundFiles.Count == 0)
        {
            sb.AppendLine("## Results");
            sb.AppendLine();
            sb.AppendLine("No files found matching the criteria.");
        }
        else
        {
            sb.AppendLine($"## Results ({handler.FoundFiles.Count} files)");
            sb.AppendLine();

            foreach (var file in handler.FoundFiles)
            {
                sb.AppendLine($"### `{file.Name}`");
                sb.AppendLine();
                sb.AppendLine($"- **Path:** `{file.Path}`");
                sb.AppendLine($"- **Size:** {_sizeParser.FormatSize(file.Size)}");
                sb.AppendLine($"- **Modified:** {file.ModifiedUtc:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"- **Created:** {file.CreatedUtc:yyyy-MM-dd HH:mm:ss}");
                
                if (!string.IsNullOrEmpty(file.Extension))
                    sb.AppendLine($"- **Extension:** `{file.Extension}`");
                
                if (!string.IsNullOrEmpty(file.Preview))
                {
                    sb.AppendLine($"- **Preview:** `{file.Preview}`");
                }
                
                if (file.Metadata?.Count > 0)
                {
                    sb.AppendLine("- **Metadata:**");
                    foreach (var kvp in file.Metadata)
                    {
                        sb.AppendLine($"  - **{kvp.Key}:** {kvp.Value}");
                    }
                }
                
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Statistics");
        sb.AppendLine();
        sb.AppendLine($"- **Files Scanned:** {handler.FilesScanned:N0}");
        sb.AppendLine($"- **Directories Scanned:** {handler.DirectoriesScanned:N0}");
        sb.AppendLine($"- **Elapsed Time:** {handler.ElapsedTime.TotalSeconds:F2} seconds");
        sb.AppendLine($"- **Results Found:** {handler.FoundFiles.Count}");
        
        if (handler.StoppedEarly)
        {
            sb.AppendLine($"- **Note:** Search stopped after finding {handler.Criteria.MaxResults} results");
        }

        if (handler.ErrorCount > 0)
        {
            sb.AppendLine($"- **Errors:** {handler.ErrorCount:N0}");
        }

        sb.AppendLine();
        sb.AppendLine($"*Generated at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");

        return sb.ToString();
    }
}
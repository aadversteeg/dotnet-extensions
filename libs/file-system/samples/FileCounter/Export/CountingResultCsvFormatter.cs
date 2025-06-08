using System.Linq;
using System.Text;
using FileCounter.Handlers;
using FileSystemSamples.Common.Export.Formatters;

namespace FileCounter.Export;

public class CountingResultCsvFormatter : CsvFormatter<ICountingHandler>
{
    public override string Format(ICountingHandler handler)
    {
        var sb = new StringBuilder();
        
        // Add summary header
        sb.AppendLine("Type,Value");
        sb.AppendLine($"Files,{handler.FileCount}");
        sb.AppendLine($"Directories,{handler.DirectoryCount}");
        sb.AppendLine($"Total Size (bytes),{handler.TotalSize}");
        sb.AppendLine($"Errors,{handler.ErrorCount}");
        sb.AppendLine($"Elapsed Time (ms),{handler.ElapsedTime.TotalMilliseconds}");
        
        // Add extension details if available
        if (handler is DetailedCounterHandler detailed && detailed.ExtensionStats.Any())
        {
            sb.AppendLine();
            sb.AppendLine("Extension,File Count,Total Size (bytes)");
            
            foreach (var ext in detailed.ExtensionStats.OrderByDescending(x => x.Value.Count))
            {
                var extensionName = string.IsNullOrEmpty(ext.Key) ? "[no extension]" : ext.Key;
                sb.AppendLine($"{EscapeCsvField(extensionName)},{ext.Value.Count},{ext.Value.TotalSize}");
            }
        }
        
        return sb.ToString();
    }
}
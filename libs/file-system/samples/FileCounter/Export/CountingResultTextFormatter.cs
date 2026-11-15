using System;
using System.Linq;
using System.Text;
using FileCounter.Handlers;
using FileSystemSamples.Common.Export.Formatters;

namespace FileCounter.Export;

public class CountingResultTextFormatter : TextFormatter<ICountingHandler>
{
    private readonly int _maxExtensions;
    
    public CountingResultTextFormatter(int maxExtensions = 20)
    {
        _maxExtensions = maxExtensions;
    }
    
    public override string Format(ICountingHandler handler)
    {
        var sb = new StringBuilder();
        sb.AppendLine("File Counting Results");
        sb.AppendLine("====================");
        sb.AppendLine();
        sb.AppendLine($"Files:       {handler.FileCount:N0}");
        sb.AppendLine($"Directories: {handler.DirectoryCount:N0}");
        sb.AppendLine($"Total Size:  {FormatBytes(handler.TotalSize)}");
        sb.AppendLine($"Errors:      {handler.ErrorCount:N0}");
        
        if (handler is DetailedCounterHandler detailed && detailed.ExtensionStats.Any())
        {
            sb.AppendLine();
            sb.AppendLine("Files by Extension:");
            sb.AppendLine("------------------");
            
            var extensions = detailed.ExtensionStats
                .OrderByDescending(x => x.Value.Count)
                .Take(_maxExtensions);
                
            foreach (var ext in extensions)
            {
                var extensionName = string.IsNullOrEmpty(ext.Key) ? "[no extension]" : ext.Key;
                sb.AppendLine($"{extensionName,-15} {ext.Value.Count,8:N0} files    {FormatBytes(ext.Value.TotalSize),10}");
            }
            
            if (detailed.ExtensionStats.Count > _maxExtensions)
            {
                sb.AppendLine($"... and {detailed.ExtensionStats.Count - _maxExtensions} more extensions");
            }
        }
        
        sb.AppendLine();
        sb.AppendLine($"Processing Time: {FormatTimeSpan(handler.ElapsedTime)}");
        
        return sb.ToString();
    }
}
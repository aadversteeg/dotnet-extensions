using System;
using System.IO;
using System.Threading.Tasks;
using FileSystemSamples.Common.Options;
using FileSystemSamples.Common.Export.Formatters;

namespace FileSystemSamples.Common.Export;

public class ExporterFactory<T> : IExporter<T>
{
    private readonly Func<ExportFormat, IFormatter<T>> _formatterFactory;
    
    public ExporterFactory(Func<ExportFormat, IFormatter<T>> formatterFactory)
    {
        _formatterFactory = formatterFactory;
    }
    
    public async Task<string?> ExportAsync(T data, ExportFormat format, string? outputPath = null)
    {
        var formatter = _formatterFactory(format);
        var content = formatter.Format(data);
        
        if (string.IsNullOrEmpty(outputPath))
        {
            return content;
        }
        
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        await File.WriteAllTextAsync(outputPath, content);
        return null;
    }
}
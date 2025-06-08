using System.Text;

namespace FileSystemSamples.Common.Export.Formatters;

public class TextFormatter<T> : BaseFormatter<T>
{
    public override string FileExtension => ".txt";
    public override string ContentType => "text/plain";
    
    public override string Format(T data)
    {
        // Default implementation uses ToString()
        // Derived classes should override for custom formatting
        return data?.ToString() ?? string.Empty;
    }
}
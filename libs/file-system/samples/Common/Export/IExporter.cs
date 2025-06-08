using System.Threading.Tasks;
using FileSystemSamples.Common.Options;

namespace FileSystemSamples.Common.Export;

public interface IExporter<T>
{
    Task<string?> ExportAsync(T data, ExportFormat format, string? outputPath = null);
}

public interface IFormatter<T>
{
    string Format(T data);
    string FileExtension { get; }
    string ContentType { get; }
}
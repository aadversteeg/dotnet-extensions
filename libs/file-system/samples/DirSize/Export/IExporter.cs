using DirSize.Handlers;
using DirSize.Models;

namespace DirSize.Export;

public interface IExporter
{
    string FileExtension { get; }
    string MimeType { get; }
    string Export(ISizeHandler handler, SizeOptions options);
}
using System.Linq;
using System.Text;
using System.Xml;
using DirSize.Handlers;
using DirSize.Models;

namespace DirSize.Export;

public class XmlExporter : IExporter
{
    public string FileExtension => ".xml";
    public string MimeType => "application/xml";

    public string Export(ISizeHandler handler, SizeOptions options)
    {
        var directories = handler.DirectorySizes
            .Where(d => d.Value.TotalSize >= (options.MinSizeBytes ?? 0))
            .Select(d => new
            {
                Path = d.Key,
                Size = d.Value.TotalSize,
                Files = d.Value.FileCount,
                Percentage = handler.TotalSize > 0 
                    ? (d.Value.TotalSize * 100.0 / handler.TotalSize) 
                    : 0
            });

        // Apply sorting
        directories = options.SortBy switch
        {
            SortBy.Size => directories.OrderByDescending(d => d.Size),
            SortBy.Name => directories.OrderBy(d => d.Path),
            SortBy.FileCount => directories.OrderByDescending(d => d.Files),
            _ => directories.OrderByDescending(d => d.Size)
        };

        var sb = new StringBuilder();
        using var writer = XmlWriter.Create(sb, new XmlWriterSettings 
        { 
            Indent = true,
            Encoding = Encoding.UTF8
        });

        writer.WriteStartDocument();
        writer.WriteStartElement("DirectorySizeReport");
        
        // Summary
        writer.WriteStartElement("Summary");
        writer.WriteElementString("TotalSize", handler.TotalSize.ToString());
        writer.WriteElementString("TotalFiles", handler.TotalFiles.ToString());
        writer.WriteElementString("TotalDirectories", handler.TotalDirectories.ToString());
        writer.WriteElementString("ProcessingTimeMs", handler.ElapsedTime.TotalMilliseconds.ToString("F0"));
        writer.WriteElementString("ErrorCount", handler.ErrorCount.ToString());
        writer.WriteEndElement();

        // Directories
        writer.WriteStartElement("Directories");
        foreach (var dir in directories.Take(options.Top))
        {
            writer.WriteStartElement("Directory");
            writer.WriteElementString("Path", dir.Path);
            writer.WriteElementString("Size", dir.Size.ToString());
            writer.WriteElementString("FileCount", dir.Files.ToString());
            writer.WriteElementString("Percentage", dir.Percentage.ToString("F2"));
            writer.WriteEndElement();
        }
        writer.WriteEndElement();

        // Errors
        if (handler.ErrorCount > 0)
        {
            writer.WriteStartElement("Errors");
            foreach (var error in handler.Errors)
            {
                writer.WriteElementString("Error", error);
            }
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();

        return sb.ToString();
    }
}
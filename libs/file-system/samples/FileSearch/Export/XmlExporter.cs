using System;
using System.Text;
using System.Xml;
using FileSearch.Handlers;

namespace FileSearch.Export;

public class XmlExporter : IExporter
{
    public string Export(ISearchHandler handler)
    {
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  "
        };

        using (var writer = XmlWriter.Create(sb, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("SearchResults");

            writer.WriteStartElement("SearchCriteria");
            writer.WriteElementString("Path", handler.Criteria.Path);
            writer.WriteElementString("Pattern", handler.Criteria.Pattern);
            if (handler.Criteria.RegexPattern != null)
                writer.WriteElementString("RegexPattern", handler.Criteria.RegexPattern);
            if (handler.Criteria.ModifiedAfter.HasValue)
                writer.WriteElementString("ModifiedAfter", handler.Criteria.ModifiedAfter.Value.ToString("yyyy-MM-dd HH:mm:ss"));
            if (handler.Criteria.ModifiedBefore.HasValue)
                writer.WriteElementString("ModifiedBefore", handler.Criteria.ModifiedBefore.Value.ToString("yyyy-MM-dd HH:mm:ss"));
            writer.WriteElementString("MaxResults", handler.Criteria.MaxResults.ToString());
            writer.WriteElementString("IncludeHidden", handler.Criteria.IncludeHidden.ToString());
            writer.WriteElementString("CaseSensitive", handler.Criteria.CaseSensitive.ToString());
            writer.WriteEndElement(); // SearchCriteria

            writer.WriteStartElement("Files");
            foreach (var file in handler.FoundFiles)
            {
                writer.WriteStartElement("File");
                writer.WriteElementString("Path", file.Path);
                writer.WriteElementString("Name", file.Name);
                writer.WriteElementString("Size", file.Size.ToString());
                if (file.Extension != null)
                    writer.WriteElementString("Extension", file.Extension);
                writer.WriteElementString("ModifiedUtc", file.ModifiedUtc.ToString("yyyy-MM-dd HH:mm:ss"));
                writer.WriteElementString("CreatedUtc", file.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss"));
                writer.WriteElementString("Attributes", file.Attributes.ToString());
                
                if (!string.IsNullOrEmpty(file.Preview))
                    writer.WriteElementString("Preview", file.Preview);

                if (file.Metadata?.Count > 0)
                {
                    writer.WriteStartElement("Metadata");
                    foreach (var kvp in file.Metadata)
                    {
                        writer.WriteElementString(kvp.Key, kvp.Value?.ToString() ?? "");
                    }
                    writer.WriteEndElement(); // Metadata
                }

                writer.WriteEndElement(); // File
            }
            writer.WriteEndElement(); // Files

            writer.WriteStartElement("Statistics");
            writer.WriteElementString("FilesScanned", handler.FilesScanned.ToString());
            writer.WriteElementString("DirectoriesScanned", handler.DirectoriesScanned.ToString());
            writer.WriteElementString("ErrorCount", handler.ErrorCount.ToString());
            writer.WriteElementString("StoppedEarly", handler.StoppedEarly.ToString());
            writer.WriteElementString("ElapsedTimeMs", handler.ElapsedTime.TotalMilliseconds.ToString("F0"));
            writer.WriteElementString("ResultsFound", handler.FoundFiles.Count.ToString());
            writer.WriteElementString("GeneratedAt", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            writer.WriteEndElement(); // Statistics

            writer.WriteEndElement(); // SearchResults
            writer.WriteEndDocument();
        }

        return sb.ToString();
    }
}
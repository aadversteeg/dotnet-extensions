using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml;
using FileCounter.Handlers;
using FileSystemSamples.Common.Options;

namespace FileCounter.Export
{
    /// <summary>
    /// Factory for creating exporters and multi-format export functionality.
    /// </summary>
    public static class CounterExporter
    {
        /// <summary>
        /// Creates an exporter for the specified format.
        /// </summary>
        /// <param name="format">The export format.</param>
        /// <returns>An exporter instance for the specified format.</returns>
        /// <exception cref="ArgumentException">Thrown when the format is not supported.</exception>
        public static IExporter CreateExporter(ExportFormat format)
        {
            return format switch
            {
                ExportFormat.Text => new TextExporter(),
                ExportFormat.Json => new JsonExporter(),
                ExportFormat.Csv => new CsvExporter(),
                ExportFormat.Xml => new XmlExporter(),
                ExportFormat.Markdown => new MarkdownExporter(),
                _ => throw new ArgumentException($"Unsupported export format: {format}", nameof(format))
            };
        }

        /// <summary>
        /// Exports the counting results and optionally writes to a file.
        /// </summary>
        /// <param name="handler">The counting handler containing the results.</param>
        /// <param name="format">The export format.</param>
        /// <param name="outputPath">Optional output file path. If null, returns the string content.</param>
        /// <returns>The exported content as a string, or null if written to file.</returns>
        public static async Task<string?> ExportAsync(ICountingHandler handler, ExportFormat format, string? outputPath = null)
        {
            var exporter = CreateExporter(format);
            var content = exporter.Export(handler);

            if (outputPath != null)
            {
                await File.WriteAllTextAsync(outputPath, content);
                return null;
            }

            return content;
        }
    }

    /// <summary>
    /// Text format exporter (human-readable console output).
    /// </summary>
    internal class TextExporter : IExporter
    {
        public string FileExtension => ".txt";
        public string MimeType => "text/plain";

        public string Export(ICountingHandler handler)
        {
            return handler.GetSummary();
        }
    }

    /// <summary>
    /// JSON format exporter for structured data processing.
    /// </summary>
    internal class JsonExporter : IExporter
    {
        public string FileExtension => ".json";
        public string MimeType => "application/json";

        public string Export(ICountingHandler handler)
        {
            var result = new
            {
                fileCount = handler.FileCount,
                directoryCount = handler.DirectoryCount,
                totalSize = handler.TotalSize,
                summary = CreateSummaryObject(handler)
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            return JsonSerializer.Serialize(result, options);
        }

        private static object CreateSummaryObject(ICountingHandler handler)
        {
            var summary = new Dictionary<string, object>
            {
                ["fileCount"] = handler.FileCount,
                ["directoryCount"] = handler.DirectoryCount,
                ["totalSize"] = handler.TotalSize,
                ["formattedSize"] = FormatSize(handler.TotalSize),
                ["elapsedTime"] = handler.ElapsedTime.TotalSeconds,
                ["formattedTime"] = FormatTime(handler.ElapsedTime),
                ["errorCount"] = handler.ErrorCount,
                ["skippedDirectories"] = handler.SkippedDirectories
            };

            // Add detailed statistics if available
            if (handler is DetailedCounterHandler detailedHandler)
            {
                summary["extensionStats"] = detailedHandler.ExtensionStats.ToDictionary(
                    kvp => string.IsNullOrEmpty(kvp.Key) ? "[no extension]" : kvp.Key,
                    kvp => new { count = kvp.Value.Count, totalSize = kvp.Value.TotalSize });
            }

            // Add summary statistics if available
            if (handler is SummaryCounterHandler summaryHandler)
            {
                summary["maxDepth"] = summaryHandler.MaxDepth;
                summary["largestFile"] = new
                {
                    name = summaryHandler.LargestFileName,
                    size = summaryHandler.LargestFileSize
                };
                summary["dateRange"] = new
                {
                    oldest = summaryHandler.OldestFileTime.ToString("O"),
                    newest = summaryHandler.NewestFileTime.ToString("O")
                };
                summary["performance"] = new
                {
                    elapsedSeconds = summaryHandler.ElapsedTime.TotalSeconds,
                    filesPerSecond = summaryHandler.ElapsedTime.TotalSeconds > 0 
                        ? handler.FileCount / summaryHandler.ElapsedTime.TotalSeconds 
                        : 0
                };
            }
            else if (handler.ElapsedTime.TotalSeconds > 0)
            {
                // Add performance info for all handlers
                summary["performance"] = new
                {
                    elapsedSeconds = handler.ElapsedTime.TotalSeconds,
                    filesPerSecond = handler.FileCount / handler.ElapsedTime.TotalSeconds
                };
            }

            return summary;
        }

        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private static string FormatTime(TimeSpan timeSpan)
        {
            if (timeSpan.TotalDays >= 1)
                return $"{timeSpan.Days}d {timeSpan.Hours}h {timeSpan.Minutes}m {timeSpan.Seconds}s";
            if (timeSpan.TotalHours >= 1)
                return $"{timeSpan.Hours}h {timeSpan.Minutes}m {timeSpan.Seconds}s";
            if (timeSpan.TotalMinutes >= 1)
                return $"{timeSpan.Minutes}m {timeSpan.Seconds}s";
            if (timeSpan.TotalSeconds >= 1)
                return $"{timeSpan.TotalSeconds:F1}s";
            return $"{timeSpan.TotalMilliseconds:F0}ms";
        }
    }

    /// <summary>
    /// CSV format exporter for spreadsheet applications.
    /// </summary>
    internal class CsvExporter : IExporter
    {
        public string FileExtension => ".csv";
        public string MimeType => "text/csv";

        public string Export(ICountingHandler handler)
        {
            var csv = new StringBuilder();

            // Basic statistics
            csv.AppendLine("Metric,Value");
            csv.AppendLine($"File Count,{handler.FileCount}");
            csv.AppendLine($"Directory Count,{handler.DirectoryCount}");
            csv.AppendLine($"Total Size (bytes),{handler.TotalSize}");

            // Add extension breakdown if available
            if (handler is DetailedCounterHandler detailedHandler && detailedHandler.ExtensionStats.Any())
            {
                csv.AppendLine();
                csv.AppendLine("Extension,File Count,Total Size (bytes)");
                
                foreach (var kvp in detailedHandler.ExtensionStats.OrderByDescending(x => x.Value.Count))
                {
                    var extension = string.IsNullOrEmpty(kvp.Key) ? "[no extension]" : kvp.Key;
                    csv.AppendLine($"{EscapeCsvField(extension)},{kvp.Value.Count},{kvp.Value.TotalSize}");
                }
            }

            // Add summary statistics if available
            if (handler is SummaryCounterHandler summaryHandler)
            {
                csv.AppendLine();
                csv.AppendLine("Summary Metric,Value");
                csv.AppendLine($"Max Depth,{summaryHandler.MaxDepth}");
                csv.AppendLine($"Largest File Name,{EscapeCsvField(summaryHandler.LargestFileName)}");
                csv.AppendLine($"Largest File Size,{summaryHandler.LargestFileSize}");
                csv.AppendLine($"Oldest File,{summaryHandler.OldestFileTime:yyyy-MM-dd HH:mm:ss}");
                csv.AppendLine($"Newest File,{summaryHandler.NewestFileTime:yyyy-MM-dd HH:mm:ss}");
                csv.AppendLine($"Processing Time (seconds),{summaryHandler.ElapsedTime.TotalSeconds:F2}");
            }

            return csv.ToString().TrimEnd();
        }

        private static string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
                return "\"\"";

            if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }

            return field;
        }
    }

    /// <summary>
    /// XML format exporter for hierarchical data representation.
    /// </summary>
    internal class XmlExporter : IExporter
    {
        public string FileExtension => ".xml";
        public string MimeType => "application/xml";

        public string Export(ICountingHandler handler)
        {
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                Encoding = Encoding.UTF8
            };

            using var stringWriter = new StringWriter();
            using var xmlWriter = XmlWriter.Create(stringWriter, settings);

            xmlWriter.WriteStartDocument();
            xmlWriter.WriteStartElement("FileCounterResults");

            // Basic statistics
            xmlWriter.WriteStartElement("BasicStatistics");
            xmlWriter.WriteElementString("FileCount", handler.FileCount.ToString(CultureInfo.InvariantCulture));
            xmlWriter.WriteElementString("DirectoryCount", handler.DirectoryCount.ToString(CultureInfo.InvariantCulture));
            xmlWriter.WriteElementString("TotalSize", handler.TotalSize.ToString(CultureInfo.InvariantCulture));
            xmlWriter.WriteEndElement(); // BasicStatistics

            // Extension statistics if available
            if (handler is DetailedCounterHandler detailedHandler && detailedHandler.ExtensionStats.Any())
            {
                xmlWriter.WriteStartElement("ExtensionStatistics");
                
                foreach (var kvp in detailedHandler.ExtensionStats.OrderByDescending(x => x.Value.Count))
                {
                    xmlWriter.WriteStartElement("Extension");
                    xmlWriter.WriteAttributeString("name", string.IsNullOrEmpty(kvp.Key) ? "[no extension]" : kvp.Key);
                    xmlWriter.WriteElementString("Count", kvp.Value.Count.ToString(CultureInfo.InvariantCulture));
                    xmlWriter.WriteElementString("TotalSize", kvp.Value.TotalSize.ToString(CultureInfo.InvariantCulture));
                    xmlWriter.WriteEndElement(); // Extension
                }
                
                xmlWriter.WriteEndElement(); // ExtensionStatistics
            }

            // Summary statistics if available
            if (handler is SummaryCounterHandler summaryHandler)
            {
                xmlWriter.WriteStartElement("SummaryStatistics");
                xmlWriter.WriteElementString("MaxDepth", summaryHandler.MaxDepth.ToString(CultureInfo.InvariantCulture));
                
                if (!string.IsNullOrEmpty(summaryHandler.LargestFileName))
                {
                    xmlWriter.WriteStartElement("LargestFile");
                    xmlWriter.WriteElementString("Name", summaryHandler.LargestFileName);
                    xmlWriter.WriteElementString("Size", summaryHandler.LargestFileSize.ToString(CultureInfo.InvariantCulture));
                    xmlWriter.WriteEndElement(); // LargestFile
                }

                xmlWriter.WriteStartElement("DateRange");
                xmlWriter.WriteElementString("Oldest", summaryHandler.OldestFileTime.ToString("O"));
                xmlWriter.WriteElementString("Newest", summaryHandler.NewestFileTime.ToString("O"));
                xmlWriter.WriteEndElement(); // DateRange

                xmlWriter.WriteStartElement("Performance");
                xmlWriter.WriteElementString("ElapsedSeconds", summaryHandler.ElapsedTime.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture));
                if (summaryHandler.ElapsedTime.TotalSeconds > 0)
                {
                    var filesPerSecond = handler.FileCount / summaryHandler.ElapsedTime.TotalSeconds;
                    xmlWriter.WriteElementString("FilesPerSecond", filesPerSecond.ToString("F2", CultureInfo.InvariantCulture));
                }
                xmlWriter.WriteEndElement(); // Performance

                xmlWriter.WriteEndElement(); // SummaryStatistics
            }

            xmlWriter.WriteEndElement(); // FileCounterResults
            xmlWriter.WriteEndDocument();

            return stringWriter.ToString();
        }
    }

    /// <summary>
    /// Markdown format exporter for documentation and reports.
    /// </summary>
    internal class MarkdownExporter : IExporter
    {
        public string FileExtension => ".md";
        public string MimeType => "text/markdown";

        public string Export(ICountingHandler handler)
        {
            var markdown = new StringBuilder();
            
            // Title
            markdown.AppendLine("# File Count Report");
            markdown.AppendLine();
            
            // Basic statistics
            markdown.AppendLine("## Summary");
            markdown.AppendLine();
            markdown.AppendLine("| Metric | Value |");
            markdown.AppendLine("|--------|-------|");
            markdown.AppendLine($"| **Files** | {handler.FileCount:N0} |");
            markdown.AppendLine($"| **Directories** | {handler.DirectoryCount:N0} |");
            markdown.AppendLine($"| **Total Size** | {FormatSize(handler.TotalSize)} |");
            markdown.AppendLine($"| **Processing Time** | {FormatTime(handler.ElapsedTime)} |");
            
            if (handler.ElapsedTime.TotalSeconds > 0)
            {
                var filesPerSecond = handler.FileCount / handler.ElapsedTime.TotalSeconds;
                markdown.AppendLine($"| **Speed** | {filesPerSecond:N0} files/second |");
            }
            
            if (handler.ErrorCount > 0)
            {
                markdown.AppendLine($"| **Errors** | {handler.ErrorCount:N0} |");
                markdown.AppendLine($"| **Skipped Directories** | {handler.SkippedDirectories:N0} |");
            }
            
            markdown.AppendLine();

            // Extension breakdown for detailed handler
            if (handler is DetailedCounterHandler detailedHandler && detailedHandler.ExtensionStats.Any())
            {
                markdown.AppendLine("## File Extensions");
                markdown.AppendLine();
                markdown.AppendLine("| Extension | Files | Size | Percentage |");
                markdown.AppendLine("|-----------|-------|------|------------|");
                
                // Show all extensions (no limit for markdown reports)
                var sortedExtensions = detailedHandler.ExtensionStats
                    .OrderByDescending(kvp => kvp.Value.Count);

                foreach (var kvp in sortedExtensions)
                {
                    var extension = string.IsNullOrEmpty(kvp.Key) ? "*no extension*" : $"`{kvp.Key}`";
                    var percentage = handler.FileCount > 0 ? (kvp.Value.Count * 100.0 / handler.FileCount) : 0;
                    markdown.AppendLine($"| {extension} | {kvp.Value.Count:N0} | {FormatSize(kvp.Value.TotalSize)} | {percentage:F1}% |");
                }
                
                markdown.AppendLine();
            }

            // Extended statistics for summary handler
            if (handler is SummaryCounterHandler summaryHandler)
            {
                markdown.AppendLine("## Detailed Statistics");
                markdown.AppendLine();
                
                if (summaryHandler.MaxDepth > 0)
                {
                    markdown.AppendLine($"- **Maximum Depth:** {summaryHandler.MaxDepth}");
                }
                
                if (summaryHandler.LargestFileSize > 0)
                {
                    markdown.AppendLine($"- **Largest File:** `{summaryHandler.LargestFileName}` ({FormatSize(summaryHandler.LargestFileSize)})");
                }
                
                if (summaryHandler.OldestFileTime != DateTime.MaxValue && summaryHandler.NewestFileTime != DateTime.MinValue)
                {
                    markdown.AppendLine($"- **Date Range:** {summaryHandler.OldestFileTime:yyyy-MM-dd} to {summaryHandler.NewestFileTime:yyyy-MM-dd}");
                }
                
                markdown.AppendLine();
            }

            // Error details if verbose
            if (handler.ErrorCount > 0 && handler.Errors.Any())
            {
                markdown.AppendLine("## Errors");
                markdown.AppendLine();
                markdown.AppendLine("The following errors occurred during processing:");
                markdown.AppendLine();
                
                var errorGroups = handler.Errors
                    .Take(10) // Show first 10 errors
                    .GroupBy(e => e)
                    .OrderByDescending(g => g.Count());
                    
                foreach (var group in errorGroups)
                {
                    if (group.Count() == 1)
                    {
                        markdown.AppendLine($"- {group.Key}");
                    }
                    else
                    {
                        markdown.AppendLine($"- {group.Key} *(occurred {group.Count()} times)*");
                    }
                }
                
                if (handler.Errors.Count > 10)
                {
                    markdown.AppendLine($"- *... and {handler.Errors.Count - 10} more errors*");
                }
                
                markdown.AppendLine();
            }

            // Footer
            markdown.AppendLine("---");
            markdown.AppendLine($"*Report generated on {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");

            return markdown.ToString().TrimEnd();
        }

        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private static string FormatTime(TimeSpan timeSpan)
        {
            if (timeSpan.TotalDays >= 1)
                return $"{timeSpan.Days}d {timeSpan.Hours}h {timeSpan.Minutes}m {timeSpan.Seconds}s";
            if (timeSpan.TotalHours >= 1)
                return $"{timeSpan.Hours}h {timeSpan.Minutes}m {timeSpan.Seconds}s";
            if (timeSpan.TotalMinutes >= 1)
                return $"{timeSpan.Minutes}m {timeSpan.Seconds}s";
            if (timeSpan.TotalSeconds >= 1)
                return $"{timeSpan.TotalSeconds:F1}s";
            return $"{timeSpan.TotalMilliseconds:F0}ms";
        }
    }
}
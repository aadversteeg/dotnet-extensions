using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.FileSystem.MimeDetective.Models;
using Ave.Extensions.Functional;
using ContentTypeAnalyzer.Models;
using FileSystemSamples.Common.Options;
using ContentTypeAnalyzer.Services;

namespace ContentTypeAnalyzer.Handlers
{
    public class DetailedAnalysisHandler : IContentTypeHandler
    {
        private readonly object _lock = new();
        private readonly List<FileSystemEntryWithContentType> _analyzedFiles = new();
        private readonly List<FileAnalysis> _analyses = new();
        private readonly AnalysisStatistics _statistics = new();
        private int _errorCount;
        private DateTime _startTime;

        public AnalysisStatistics Statistics => _statistics;
        public int AnalyzedFiles => _analyzedFiles.Count;
        public int ErrorCount => _errorCount;
        
        public List<FileAnalysis> Analyses => _analyses;

        public Task<Result<Unit, FileSystemError>> OnTraversalStartAsync(CanonicalPath rootPath, CancellationToken cancellationToken = default)
        {
            _startTime = DateTime.UtcNow;
            return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
        }

        public Task<Result<TraversalControl, FileSystemError>> OnDirectoryEnterAsync(FileSystemEntryWithContentType directory, TraversalContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }

        public Task<Result<TraversalControl, FileSystemError>> OnFileAsync(FileSystemEntryWithContentType file, TraversalContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var analysis = CreateDetailedAnalysis(file);
                var category = CategoryClassifier.DetermineCategory(file.ContentType);
                
                lock (_lock)
                {
                    _analyzedFiles.Add(file);
                    _analyses.Add(analysis);
                    
                    _statistics.TotalFiles++;
                    _statistics.TotalSize += file.Size ?? 0;
                    
                    if (!string.IsNullOrEmpty(file.ContentType))
                    {
                        _statistics.ContentTypeCounts[file.ContentType] = 
                            _statistics.ContentTypeCounts.GetValueOrDefault(file.ContentType, 0) + 1;
                    }
                    
                    _statistics.CategoryCounts[category] = 
                        _statistics.CategoryCounts.GetValueOrDefault(category, 0) + 1;

                    var confidenceLevel = GetConfidenceLevel(file.Confidence);
                    _statistics.ConfidenceLevels[confidenceLevel] = 
                        _statistics.ConfidenceLevels.GetValueOrDefault(confidenceLevel, 0) + 1;
                }
            }
            catch (Exception)
            {
                Interlocked.Increment(ref _errorCount);
            }

            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }

        public Task<Result<TraversalControl, FileSystemError>> OnDirectoryAsync(FileSystemEntryWithContentType directory, TraversalContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }

        public Task<Result<Unit, FileSystemError>> OnTraversalCompleteAsync(TraversalSummary summary, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
        }

        private FileAnalysis CreateDetailedAnalysis(FileSystemEntryWithContentType file)
        {
            var category = CategoryClassifier.DetermineCategory(file.ContentType);
            var expectedExtensions = CategoryClassifier.GetExpectedExtensions(file.ContentType);
            var currentExtension = file.Extension ?? "";
            var isCorrectlyNamed = expectedExtensions.Contains(currentExtension, StringComparer.OrdinalIgnoreCase);
            
            return new FileAnalysis
            {
                Path = file.Path.Value,
                OriginalExtension = currentExtension,
                DetectedContentType = file.ContentType ?? "unknown",
                FileTypeDescription = GetFileTypeDescription(file.ContentType),
                Confidence = file.Confidence,
                AlternativeExtensions = expectedExtensions,
                Category = category,
                Size = file.Size ?? 0,
                Modified = file.ModifiedUtc,
                IsCorrectlyNamed = isCorrectlyNamed,
                SecurityAssessment = AssessSecurityRisk(file.ContentType, currentExtension),
                Metadata = ExtractMetadata(file)
            };
        }

        private string GetFileTypeDescription(string? contentType)
        {
            if (string.IsNullOrEmpty(contentType))
                return "Unknown file type";

            return contentType.ToLowerInvariant() switch
            {
                "image/jpeg" => "JPEG Image",
                "image/png" => "PNG Image",
                "image/gif" => "GIF Animation",
                "image/bmp" => "Bitmap Image",
                "image/tiff" => "TIFF Image",
                "image/webp" => "WebP Image",
                
                "audio/mpeg" => "MP3 Audio",
                "audio/wav" => "WAV Audio",
                "audio/flac" => "FLAC Lossless Audio",
                "audio/ogg" => "Ogg Vorbis Audio",
                "audio/mp4" => "AAC Audio",
                
                "video/mp4" => "MP4 Video",
                "video/avi" => "AVI Video",
                "video/x-matroska" => "Matroska Video",
                "video/quicktime" => "QuickTime Video",
                "video/x-ms-wmv" => "Windows Media Video",
                
                "application/pdf" => "PDF Document",
                "application/msword" => "Microsoft Word Document",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "Microsoft Word Document (OOXML)",
                "application/vnd.ms-excel" => "Microsoft Excel Spreadsheet",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "Microsoft Excel Spreadsheet (OOXML)",
                "application/vnd.ms-powerpoint" => "Microsoft PowerPoint Presentation",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation" => "Microsoft PowerPoint Presentation (OOXML)",
                "application/rtf" => "Rich Text Format Document",
                
                "application/zip" => "ZIP Archive",
                "application/x-rar-compressed" => "RAR Archive",
                "application/x-7z-compressed" => "7-Zip Archive",
                "application/gzip" => "Gzip Compressed File",
                "application/x-tar" => "TAR Archive",
                
                "application/x-msdownload" => "Windows Executable",
                "application/x-dosexec" => "DOS Executable",
                
                "text/plain" => "Plain Text",
                "text/html" => "HTML Document",
                "text/css" => "CSS Stylesheet",
                "text/javascript" => "JavaScript Code",
                "application/json" => "JSON Data",
                "application/xml" => "XML Document",
                
                _ => $"File of type {contentType}"
            };
        }

        private string AssessSecurityRisk(string? contentType, string currentExtension)
        {
            if (string.IsNullOrEmpty(contentType))
                return "Unknown";

            if (IsExecutableContentType(contentType) && !IsExecutableExtension(currentExtension))
                return "High - Executable content disguised";

            if (IsScriptContentType(contentType) && IsDocumentExtension(currentExtension))
                return "Medium - Script content in document";

            var expectedExtensions = CategoryClassifier.GetExpectedExtensions(contentType);
            if (expectedExtensions.Length > 0 && !expectedExtensions.Contains(currentExtension, StringComparer.OrdinalIgnoreCase))
                return "Low - Extension mismatch";

            return "None";
        }

        private Dictionary<string, object> ExtractMetadata(FileSystemEntryWithContentType file)
        {
            var metadata = new Dictionary<string, object>
            {
                ["fileName"] = file.Name,
                ["fileSize"] = file.Size ?? 0,
                ["lastModified"] = file.ModifiedUtc,
                ["contentType"] = file.ContentType ?? "unknown",
                ["confidence"] = file.Confidence
            };

            var category = CategoryClassifier.DetermineCategory(file.ContentType);
            metadata["category"] = category.ToString();
            metadata["subType"] = CategoryClassifier.GetContentSubType(file.ContentType);

            return metadata;
        }

        public async Task ExportAsync(string outputPath, ExportFormat format)
        {
            var analysisMetadata = new
            {
                startTime = _startTime,
                endTime = DateTime.UtcNow,
                definitionPack = "default",
                filesAnalyzed = _statistics.TotalFiles,
                totalSize = _statistics.TotalSize
            };

            switch (format)
            {
                case ExportFormat.Json:
                    await ExportToJsonAsync(outputPath, analysisMetadata);
                    break;
                case ExportFormat.Csv:
                    await ExportToCsvAsync(outputPath);
                    break;
                case ExportFormat.Xml:
                    await ExportToXmlAsync(outputPath, analysisMetadata);
                    break;
                case ExportFormat.Text:
                    await ExportToTextAsync(outputPath, analysisMetadata);
                    break;
                default:
                    throw new ArgumentException($"Unsupported export format: {format}");
            }
        }

        private async Task ExportToJsonAsync(string outputPath, object analysisMetadata)
        {
            var exportData = new
            {
                analysisMetadata,
                statistics = new
                {
                    byContentType = _statistics.ContentTypeCounts.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new { count = kvp.Value, totalSize = 0L }
                    ),
                    byCategory = _statistics.CategoryCounts.ToDictionary(
                        kvp => kvp.Key.ToString(),
                        kvp => new { 
                            count = kvp.Value, 
                            percentage = _statistics.TotalFiles > 0 ? (double)kvp.Value / _statistics.TotalFiles * 100 : 0 
                        }
                    ),
                    confidenceDistribution = _statistics.ConfidenceLevels
                },
                files = _analyses
            };

            var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await File.WriteAllTextAsync(outputPath, json);
        }

        private async Task ExportToCsvAsync(string outputPath)
        {
            var csv = new StringBuilder();
            csv.AppendLine("Path,OriginalExtension,DetectedContentType,FileTypeDescription,Confidence,Category,Size,Modified,IsCorrectlyNamed,SecurityAssessment");

            foreach (var analysis in _analyses)
            {
                csv.AppendLine($@"""{analysis.Path}"",{analysis.OriginalExtension},{analysis.DetectedContentType},""{analysis.FileTypeDescription}"",{analysis.Confidence},{analysis.Category},{analysis.Size},{analysis.Modified:yyyy-MM-dd HH:mm:ss},{analysis.IsCorrectlyNamed},""{analysis.SecurityAssessment}""");
            }

            await File.WriteAllTextAsync(outputPath, csv.ToString());
        }

        private async Task ExportToXmlAsync(string outputPath, object analysisMetadata)
        {
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n"
            };

            using var writer = XmlWriter.Create(outputPath, settings);
            
            writer.WriteStartDocument();
            writer.WriteStartElement("ContentTypeAnalysis");
            
            writer.WriteStartElement("Metadata");
            writer.WriteElementString("FilesAnalyzed", _statistics.TotalFiles.ToString());
            writer.WriteElementString("TotalSize", _statistics.TotalSize.ToString());
            writer.WriteElementString("StartTime", _startTime.ToString("O"));
            writer.WriteElementString("EndTime", DateTime.UtcNow.ToString("O"));
            writer.WriteEndElement();
            
            writer.WriteStartElement("Files");
            foreach (var analysis in _analyses)
            {
                writer.WriteStartElement("File");
                writer.WriteElementString("Path", analysis.Path);
                writer.WriteElementString("OriginalExtension", analysis.OriginalExtension);
                writer.WriteElementString("DetectedContentType", analysis.DetectedContentType);
                writer.WriteElementString("FileTypeDescription", analysis.FileTypeDescription);
                writer.WriteElementString("Confidence", analysis.Confidence.ToString());
                writer.WriteElementString("Category", analysis.Category.ToString());
                writer.WriteElementString("Size", analysis.Size.ToString());
                writer.WriteElementString("Modified", analysis.Modified.ToString("O"));
                writer.WriteElementString("IsCorrectlyNamed", analysis.IsCorrectlyNamed.ToString());
                writer.WriteElementString("SecurityAssessment", analysis.SecurityAssessment);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
            
            writer.WriteEndElement();
            writer.WriteEndDocument();
            
            await writer.FlushAsync();
        }

        private async Task ExportToTextAsync(string outputPath, object analysisMetadata)
        {
            var text = new StringBuilder();
            text.AppendLine("Content Type Analysis Report");
            text.AppendLine("============================");
            text.AppendLine();
            
            text.AppendLine($"Analysis completed: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            text.AppendLine($"Total files analyzed: {_statistics.TotalFiles:N0}");
            text.AppendLine($"Total size: {FormatBytes(_statistics.TotalSize)}");
            text.AppendLine();
            
            text.AppendLine("Content Type Distribution:");
            text.AppendLine("-------------------------");
            foreach (var kvp in _statistics.ContentTypeCounts.OrderByDescending(x => x.Value))
            {
                text.AppendLine($"{kvp.Key,-40} {kvp.Value,8} files");
            }
            text.AppendLine();
            
            text.AppendLine("Category Distribution:");
            text.AppendLine("--------------------");
            foreach (var kvp in _statistics.CategoryCounts.OrderByDescending(x => x.Value))
            {
                var percentage = _statistics.TotalFiles > 0 ? (double)kvp.Value / _statistics.TotalFiles * 100 : 0;
                text.AppendLine($"{kvp.Key,-12} {kvp.Value,8} files ({percentage:F1}%)");
            }
            text.AppendLine();
            
            text.AppendLine("Detailed File Analysis:");
            text.AppendLine("======================");
            foreach (var analysis in _analyses.Take(100))
            {
                text.AppendLine($"Path: {analysis.Path}");
                text.AppendLine($"  Type: {analysis.FileTypeDescription}");
                text.AppendLine($"  Content Type: {analysis.DetectedContentType}");
                text.AppendLine($"  Category: {analysis.Category}");
                text.AppendLine($"  Size: {FormatBytes(analysis.Size)}");
                text.AppendLine($"  Confidence: {analysis.Confidence:F2}");
                text.AppendLine($"  Correctly Named: {analysis.IsCorrectlyNamed}");
                text.AppendLine($"  Security: {analysis.SecurityAssessment}");
                text.AppendLine();
            }
            
            if (_analyses.Count > 100)
            {
                text.AppendLine($"... and {_analyses.Count - 100} more files");
            }

            await File.WriteAllTextAsync(outputPath, text.ToString());
        }

        private static string GetConfidenceLevel(double confidence)
        {
            return confidence switch
            {
                >= 0.8 => "High",
                >= 0.5 => "Medium",
                _ => "Low"
            };
        }

        private static bool IsExecutableContentType(string contentType)
        {
            return contentType.ToLowerInvariant() switch
            {
                "application/x-msdownload" => true,
                "application/x-dosexec" => true,
                "application/octet-stream" => true,
                _ => false
            };
        }

        private static bool IsScriptContentType(string contentType)
        {
            return contentType.ToLowerInvariant() switch
            {
                "text/javascript" => true,
                "application/javascript" => true,
                "text/vbscript" => true,
                "application/x-powershell" => true,
                _ => false
            };
        }

        private static bool IsExecutableExtension(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".exe" or ".dll" or ".com" or ".bat" or ".cmd" or ".scr" => true,
                _ => false
            };
        }

        private static bool IsDocumentExtension(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".doc" or ".docx" or ".pdf" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".rtf" => true,
                _ => false
            };
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            
            return $"{number:N1} {suffixes[counter]}";
        }

    }
}
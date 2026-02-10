using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.FileSystem.MimeDetective.Models;
using Ave.Extensions.Functional;
using ContentTypeAnalyzer.Models;
using ContentTypeAnalyzer.Services;

namespace ContentTypeAnalyzer.Handlers
{
    public class MisnamedFilesHandler : IContentTypeHandler
    {
        private readonly object _lock = new();
        private readonly List<MismatchedFile> _mismatchedFiles = new();
        private int _analyzedFiles = 0;
        private int _errorCount = 0;

        public record MismatchedFile(
            string Path,
            string CurrentExtension,
            string DetectedContentType,
            string[] ExpectedExtensions,
            double Confidence,
            string SecurityRisk
        );

        public List<MismatchedFile> MismatchedFiles
        {
            get
            {
                lock (_lock)
                {
                    return new List<MismatchedFile>(_mismatchedFiles);
                }
            }
        }

        public AnalysisStatistics Statistics
        {
            get
            {
                lock (_lock)
                {
                    return new AnalysisStatistics
                    {
                        TotalFiles = _analyzedFiles,
                        ErrorCount = _errorCount
                    };
                }
            }
        }

        public int AnalyzedFiles
        {
            get
            {
                lock (_lock)
                {
                    return _analyzedFiles;
                }
            }
        }

        public int ErrorCount
        {
            get
            {
                lock (_lock)
                {
                    return _errorCount;
                }
            }
        }

        public Task<Result<Unit, Error>> OnTraversalStartAsync(
            CanonicalPath rootPath, 
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
        }

        public Task<Result<TraversalControl, Error>> OnDirectoryEnterAsync(
            FileSystemEntryWithContentType directory, 
            TraversalContext context, 
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }

        public Task<Result<TraversalControl, Error>> OnFileAsync(
            FileSystemEntryWithContentType file, 
            TraversalContext context, 
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _analyzedFiles++;

                if (!string.IsNullOrEmpty(file.ContentType) && 
                    !string.IsNullOrEmpty(file.Extension))
                {
                    var expectedExtensions = GetExpectedExtensions(file.ContentType);
                    var currentExtension = file.Extension.ToLowerInvariant();

                    if (expectedExtensions.Length > 0 && !expectedExtensions.Contains(currentExtension))
                    {
                        var securityRisk = AssessSecurityRisk(currentExtension, file.ContentType);
                        
                        _mismatchedFiles.Add(new MismatchedFile(
                            file.Path.Value,
                            file.Extension,
                            file.ContentType,
                            expectedExtensions,
                            file.Confidence,
                            securityRisk
                        ));
                    }
                }
            }

            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }

        public Task<Result<TraversalControl, Error>> OnDirectoryAsync(
            FileSystemEntryWithContentType directory, 
            TraversalContext context, 
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }

        public Task<Result<Unit, Error>> OnTraversalCompleteAsync(
            TraversalSummary summary, 
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
        }

        private static string[] GetExpectedExtensions(string contentType)
        {
            return contentType.ToLowerInvariant() switch
            {
                "image/jpeg" => new[] { ".jpg", ".jpeg" },
                "image/png" => new[] { ".png" },
                "image/gif" => new[] { ".gif" },
                "image/bmp" => new[] { ".bmp" },
                "image/webp" => new[] { ".webp" },
                "image/svg+xml" => new[] { ".svg" },
                "application/pdf" => new[] { ".pdf" },
                "text/plain" => new[] { ".txt" },
                "text/html" => new[] { ".html", ".htm" },
                "application/json" => new[] { ".json" },
                "application/xml" => new[] { ".xml" },
                "application/zip" => new[] { ".zip" },
                "application/x-rar" => new[] { ".rar" },
                "application/x-7z-compressed" => new[] { ".7z" },
                "audio/mpeg" => new[] { ".mp3" },
                "audio/wav" => new[] { ".wav" },
                "video/mp4" => new[] { ".mp4" },
                "video/x-msvideo" => new[] { ".avi" },
                "application/x-msdownload" => new[] { ".exe", ".dll", ".com" },
                "application/x-sh" => new[] { ".sh", ".bash" },
                "application/x-python" => new[] { ".py" },
                _ => Array.Empty<string>()
            };
        }

        private static string AssessSecurityRisk(string extension, string contentType)
        {
            var isExecutableContent = IsExecutableContent(contentType);
            var isExecutableExtension = IsExecutableExtension(extension);
            var isScriptContent = IsScriptContent(contentType);
            var isDocumentExtension = IsDocumentExtension(extension);

            if (isExecutableContent && !isExecutableExtension)
                return "High";

            if (isScriptContent && isDocumentExtension)
                return "Medium";

            return "Low";
        }

        private static bool IsExecutableContent(string contentType)
        {
            var type = contentType.ToLowerInvariant();
            return type == "application/x-msdownload" ||
                   type == "application/x-executable" ||
                   type.Contains("executable");
        }

        private static bool IsExecutableExtension(string extension)
        {
            var ext = extension.ToLowerInvariant();
            return ext == ".exe" || ext == ".dll" || ext == ".com" || ext == ".bat";
        }

        private static bool IsScriptContent(string contentType)
        {
            var type = contentType.ToLowerInvariant();
            return type == "application/x-sh" ||
                   type == "application/x-python" ||
                   type == "application/javascript" ||
                   type.Contains("script");
        }

        private static bool IsDocumentExtension(string extension)
        {
            var ext = extension.ToLowerInvariant();
            return ext == ".doc" || ext == ".docx" || ext == ".pdf" || 
                   ext == ".txt" || ext == ".rtf";
        }
    }
}
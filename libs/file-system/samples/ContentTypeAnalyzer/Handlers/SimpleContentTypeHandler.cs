using System.Collections.Generic;
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
    public class SimpleContentTypeHandler : IContentTypeHandler
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, int> _contentTypeCounts = new();
        private readonly Dictionary<FileCategory, int> _categoryCounts = new();
        private readonly Dictionary<string, int> _confidenceLevels = new();
        private long _totalSize = 0;
        private int _analyzedFiles = 0;
        private int _errorCount = 0;

        public AnalysisStatistics Statistics
        {
            get
            {
                lock (_lock)
                {
                    return new AnalysisStatistics
                    {
                        TotalFiles = _analyzedFiles,
                        ErrorCount = _errorCount,
                        TotalSize = _totalSize,
                        ContentTypeCounts = new Dictionary<string, int>(_contentTypeCounts),
                        CategoryCounts = new Dictionary<FileCategory, int>(_categoryCounts),
                        ConfidenceLevels = new Dictionary<string, int>(_confidenceLevels)
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
                _totalSize += file.Size ?? 0;

                var contentType = file.ContentType ?? "unknown";
                _contentTypeCounts[contentType] = _contentTypeCounts.GetValueOrDefault(contentType, 0) + 1;

                var category = CategoryClassifier.DetermineCategory(contentType);
                _categoryCounts[category] = _categoryCounts.GetValueOrDefault(category, 0) + 1;

                var confidenceLevel = file.Confidence switch
                {
                    >= 0.9 => "High",
                    >= 0.7 => "Medium",
                    _ => "Low"
                };
                _confidenceLevels[confidenceLevel] = _confidenceLevels.GetValueOrDefault(confidenceLevel, 0) + 1;
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
    }
}
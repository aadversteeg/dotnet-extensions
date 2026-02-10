using System;
using System.Collections.Generic;
using System.IO;
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
    public class OrganizeFilesHandler : IContentTypeHandler
    {
        private readonly object _lock = new();
        private readonly List<FileSystemEntryWithContentType> _analyzedFiles = new();
        private readonly Dictionary<FileCategory, List<FileSystemEntryWithContentType>> _filesByCategory = new();
        private readonly List<FileMove> _proposedMoves = new();
        private readonly AnalysisStatistics _statistics = new();
        private int _errorCount;

        public AnalysisStatistics Statistics => _statistics;
        public int AnalyzedFiles => _analyzedFiles.Count;
        public int ErrorCount => _errorCount;
        
        public Dictionary<FileCategory, List<FileSystemEntryWithContentType>> FilesByCategory => _filesByCategory;
        public List<FileMove> ProposedMoves => _proposedMoves;

        public Task<Result<Unit, Error>> OnTraversalStartAsync(CanonicalPath rootPath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
        }

        public Task<Result<TraversalControl, Error>> OnDirectoryEnterAsync(FileSystemEntryWithContentType directory, TraversalContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }

        public Task<Result<TraversalControl, Error>> OnFileAsync(FileSystemEntryWithContentType file, TraversalContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var category = CategoryClassifier.DetermineCategory(file.ContentType);
                
                lock (_lock)
                {
                    _analyzedFiles.Add(file);
                    _statistics.TotalFiles++;
                    _statistics.TotalSize += file.Size ?? 0;
                    
                    if (!string.IsNullOrEmpty(file.ContentType))
                    {
                        _statistics.ContentTypeCounts[file.ContentType] = 
                            _statistics.ContentTypeCounts.GetValueOrDefault(file.ContentType, 0) + 1;
                    }
                    
                    _statistics.CategoryCounts[category] = 
                        _statistics.CategoryCounts.GetValueOrDefault(category, 0) + 1;

                    if (!_filesByCategory.ContainsKey(category))
                        _filesByCategory[category] = new List<FileSystemEntryWithContentType>();
                    
                    _filesByCategory[category].Add(file);
                }
            }
            catch (Exception)
            {
                Interlocked.Increment(ref _errorCount);
            }

            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }

        public Task<Result<TraversalControl, Error>> OnDirectoryAsync(FileSystemEntryWithContentType directory, TraversalContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }

        public Task<Result<Unit, Error>> OnTraversalCompleteAsync(TraversalSummary summary, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
        }

        public void GenerateOrganizationPlan(string outputRoot)
        {
            lock (_lock)
            {
                _proposedMoves.Clear();
                
                foreach (var file in _analyzedFiles)
                {
                    var targetPath = GenerateTargetPath(file, outputRoot);
                    var requiresRename = DetermineIfRenameRequired(file);
                    var reason = GenerateReason(file, requiresRename);
                    
                    _proposedMoves.Add(new FileMove(
                        file.Path.Value,
                        targetPath,
                        reason,
                        requiresRename
                    ));
                }
            }
        }

        private string GenerateTargetPath(FileSystemEntryWithContentType file, string outputRoot)
        {
            var category = CategoryClassifier.DetermineCategory(file.ContentType);
            var subType = CategoryClassifier.GetContentSubType(file.ContentType);
            var properExtension = GetProperExtension(file);
            
            // Organize by: Category/SubType/YYYY-MM/filename.ext
            var date = file.ModifiedUtc.ToString("yyyy-MM");
            var fileName = System.IO.Path.GetFileNameWithoutExtension(file.Name) + properExtension;
            
            return System.IO.Path.Combine(outputRoot, category.ToString(), subType, date, fileName);
        }

        private string GetProperExtension(FileSystemEntryWithContentType file)
        {
            var expectedExtensions = CategoryClassifier.GetExpectedExtensions(file.ContentType);
            if (expectedExtensions.Length > 0)
            {
                var currentExtension = file.Extension ?? "";
                if (!expectedExtensions.Contains(currentExtension, StringComparer.OrdinalIgnoreCase))
                {
                    return expectedExtensions[0]; // Use the first/primary extension
                }
            }
            
            return file.Extension ?? ""; // Keep current extension if it's correct
        }

        private bool DetermineIfRenameRequired(FileSystemEntryWithContentType file)
        {
            var expectedExtensions = CategoryClassifier.GetExpectedExtensions(file.ContentType);
            if (expectedExtensions.Length == 0)
                return false;
                
            var currentExtension = file.Extension ?? "";
            return !expectedExtensions.Contains(currentExtension, StringComparer.OrdinalIgnoreCase);
        }

        private string GenerateReason(FileSystemEntryWithContentType file, bool requiresRename)
        {
            var category = CategoryClassifier.DetermineCategory(file.ContentType);
            var reason = $"Organize into {category} category";
            
            if (requiresRename)
            {
                var properExtension = GetProperExtension(file);
                reason += $" and rename to proper extension {properExtension}";
            }
            
            return reason;
        }

    }
}
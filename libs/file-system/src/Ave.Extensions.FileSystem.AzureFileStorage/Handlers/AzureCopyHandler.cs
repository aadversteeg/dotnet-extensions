using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.AzureFileStorage.Handlers
{
    /// <summary>
    /// Handles copying files during directory traversal for Azure File Storage.
    /// </summary>
    internal class AzureCopyHandler : ITraversalHandler<FileSystemEntry>
    {
        private readonly AzureFileSystem _fileSystem;
        private readonly CanonicalPath _sourceRoot;
        private readonly CanonicalPath _destinationRoot;
        private readonly List<FileSystemError> _errors;
        private int _filesProcessed;
        private int _directoriesProcessed;
        
        public AzureCopyHandler(
            AzureFileSystem fileSystem,
            CanonicalPath sourceRoot,
            CanonicalPath destinationRoot)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _sourceRoot = sourceRoot;
            _destinationRoot = destinationRoot;
            _errors = [];
        }
        
        public List<FileSystemError> Errors => _errors;
        public int FilesProcessed => _filesProcessed;
        public int DirectoriesProcessed => _directoriesProcessed;
        
        public Task<Result<Unit, FileSystemError>> OnTraversalStartAsync(
            CanonicalPath rootPath,
            CancellationToken cancellationToken = default)
        {
            // Ensure destination root exists
            return _fileSystem.CreateDirectoryAsync(_destinationRoot, cancellationToken);
        }
        
        public async Task<Result<TraversalControl, FileSystemError>> OnFileAsync(
            FileSystemEntry file,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Calculate relative path and destination
                var relativePath = GetRelativePath(_sourceRoot, file.Path);
                var destinationPath = CombinePaths(_destinationRoot, relativePath);
                
                // Copy the file
                var copyResult = await _fileSystem.CopyFileAsync(
                    file.Path,
                    destinationPath,
                    overwrite: false,
                    cancellationToken);
                
                if (copyResult.IsSuccess)
                {
                    _filesProcessed++;
                }
                else
                {
                    _errors.Add(copyResult.Error);
                    // Continue processing other files
                }
                
                return Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue);
            }
            catch (Exception ex)
            {
                var error = FileSystemError.FromException(ex, file.Path.Value);
                _errors.Add(error);
                return Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue);
            }
        }
        
        public async Task<Result<TraversalControl, FileSystemError>> OnDirectoryEnterAsync(
            FileSystemEntry directory,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            if (context.CurrentDepth > 0) // Skip root directory (already created)
            {
                var relativePath = GetRelativePath(_sourceRoot, directory.Path);
                var destinationPath = CombinePaths(_destinationRoot, relativePath);
                
                var createResult = await _fileSystem.CreateDirectoryAsync(destinationPath, cancellationToken);
                if (createResult.IsSuccess)
                {
                    _directoriesProcessed++;
                }
                else if (createResult.Error.Type != FileSystemErrorType.DirectoryAlreadyExists)
                {
                    _errors.Add(createResult.Error);
                }
            }
            
            return Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue);
        }
        
        public Task<Result<TraversalControl, FileSystemError>> OnDirectoryAsync(
            FileSystemEntry directory,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }
        
        public Task<Result<TraversalControl, FileSystemError>> OnDirectoryLeaveAsync(
            FileSystemEntry directory,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
        }
        
        public Task<Result<Unit, FileSystemError>> OnTraversalCompleteAsync(
            TraversalSummary summary,
            CancellationToken cancellationToken = default)
        {
            if (_errors.Count > 0)
            {
                var errorMessage = $"Directory copy completed with {_errors.Count} errors. " +
                                 $"Files processed: {_filesProcessed}, Directories: {_directoriesProcessed}";
                return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                    new FileSystemError(errorMessage, _sourceRoot.Value, FileSystemErrorType.PartialFailure)));
            }
            
            return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
        }
        
        private string GetRelativePath(CanonicalPath basePath, CanonicalPath fullPath)
        {
            var baseValue = basePath.Value.TrimEnd('/');
            var fullValue = fullPath.Value;
            
            if (!fullValue.StartsWith(baseValue + "/"))
                return Path.GetFileName(fullValue);
            
            return fullValue.Substring(baseValue.Length + 1);
        }
        
        private CanonicalPath CombinePaths(CanonicalPath basePath, string relativePath)
        {
            var combined = basePath.Value.TrimEnd('/') + "/" + relativePath;
            return CanonicalPath.Create(combined).Value;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.AzureFileStorage.Handlers
{
    /// <summary>
    /// Handles deleting files during directory traversal for Azure File Storage.
    /// </summary>
    internal class AzureDeleteHandler : ITraversalHandler<FileSystemEntry>
    {
        private readonly AzureFileSystem _fileSystem;
        private readonly List<FileSystemError> _errors;
        private readonly List<CanonicalPath> _directoriesToDelete;
        private int _filesDeleted;
        private int _directoriesDeleted;
        
        public AzureDeleteHandler(AzureFileSystem fileSystem)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _errors = new List<FileSystemError>();
            _directoriesToDelete = new List<CanonicalPath>();
        }
        
        public List<FileSystemError> Errors => _errors;
        public int FilesDeleted => _filesDeleted;
        public int DirectoriesDeleted => _directoriesDeleted;
        
        public Task<Result<Unit, FileSystemError>> OnTraversalStartAsync(
            CanonicalPath rootPath,
            CancellationToken cancellationToken = default)
        {
            // Add root to list of directories to delete (will be deleted last)
            _directoriesToDelete.Add(rootPath);
            return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
        }
        
        public async Task<Result<TraversalControl, FileSystemError>> OnFileAsync(
            FileSystemEntry file,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Delete the file
                var deleteResult = await _fileSystem.DeleteFileAsync(file.Path, cancellationToken);
                
                if (deleteResult.IsSuccess)
                {
                    _filesDeleted++;
                }
                else
                {
                    _errors.Add(deleteResult.Error);
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
        
        public Task<Result<TraversalControl, FileSystemError>> OnDirectoryEnterAsync(
            FileSystemEntry directory,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            // Add to list for deletion after all files are deleted
            if (context.CurrentDepth > 0)
            {
                _directoriesToDelete.Add(directory.Path);
            }
            
            return Task.FromResult(Result<TraversalControl, FileSystemError>.Success(TraversalControl.Continue));
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
            // Delete directories in reverse order (deepest first)
            for (int i = _directoriesToDelete.Count - 1; i >= 0; i--)
            {
                try
                {
                    // In Azure, directories are virtual, so we just track the deletion
                    _directoriesDeleted++;
                }
                catch (Exception ex)
                {
                    var error = FileSystemError.FromException(ex, _directoriesToDelete[i].Value);
                    _errors.Add(error);
                }
            }
            
            if (_errors.Count > 0)
            {
                var errorMessage = $"Directory deletion completed with {_errors.Count} errors. " +
                                 $"Files deleted: {_filesDeleted}, Directories: {_directoriesDeleted}";
                return Task.FromResult(Result<Unit, FileSystemError>.Failure(
                    new FileSystemError(errorMessage, null, FileSystemErrorType.PartialFailure)));
            }
            
            return Task.FromResult(Result<Unit, FileSystemError>.Success(Unit.Value));
        }
    }
}
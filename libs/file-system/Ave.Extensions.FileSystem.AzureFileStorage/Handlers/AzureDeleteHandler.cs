using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.ErrorPaths;
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
        private readonly List<Error> _errors;
        private readonly List<CanonicalPath> _directoriesToDelete;
        private int _filesDeleted;
        private int _directoriesDeleted;
        
        public AzureDeleteHandler(AzureFileSystem fileSystem)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _errors = [];
            _directoriesToDelete = [];
        }
        
        public List<Error> Errors => _errors;
        public int FilesDeleted => _filesDeleted;
        public int DirectoriesDeleted => _directoriesDeleted;
        
        public Task<Result<Unit, Error>> OnTraversalStartAsync(
            CanonicalPath rootPath,
            CancellationToken cancellationToken = default)
        {
            // Add root to list of directories to delete (will be deleted last)
            _directoriesToDelete.Add(rootPath);
            return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
        }
        
        public async Task<Result<TraversalControl, Error>> OnFileAsync(
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
                
                return Result<TraversalControl, Error>.Success(TraversalControl.Continue);
            }
            catch (Exception ex)
            {
                var error = FileSystemErrors.FromException(ex, file.Path.Value);
                _errors.Add(error);
                return Result<TraversalControl, Error>.Success(TraversalControl.Continue);
            }
        }
        
        public Task<Result<TraversalControl, Error>> OnDirectoryEnterAsync(
            FileSystemEntry directory,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            // Add to list for deletion after all files are deleted
            if (context.CurrentDepth > 0)
            {
                _directoriesToDelete.Add(directory.Path);
            }
            
            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }
        
        public Task<Result<TraversalControl, Error>> OnDirectoryAsync(
            FileSystemEntry directory,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }
        
        public Task<Result<TraversalControl, Error>> OnDirectoryLeaveAsync(
            FileSystemEntry directory,
            TraversalContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }
        
        public Task<Result<Unit, Error>> OnTraversalCompleteAsync(
            TraversalSummary summary,
            CancellationToken cancellationToken = default)
        {
            // In Azure File Storage, directories are virtual and are automatically 
            // removed when they become empty (all files deleted)
            // We just need to count them as "deleted" for reporting purposes
            _directoriesDeleted = _directoriesToDelete.Count;
            
            if (_errors.Count > 0)
            {
                var errorMessage = $"Directory deletion completed with {_errors.Count} errors. " +
                                 $"Files deleted: {_filesDeleted}, Directories: {_directoriesDeleted}";
                return Task.FromResult(Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.IO.PartialFailure, errorMessage)));
            }
            
            return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Progress;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem.Extensions
{
    /// <summary>
    /// Extension methods for copy operations.
    /// </summary>
    public static class FileSystemCopyExtensions
    {
        /// <summary>
        /// Copies a file or directory with options.
        /// </summary>
        public static async Task<Result<Unit, Error>> CopyAsync(
            this IFileSystem fileSystem,
            CanonicalPath source,
            CanonicalPath destination,
            CopyOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (fileSystem == null)
                throw new ArgumentNullException(nameof(fileSystem));
            
            options ??= CopyOptions.Default;
            
            // Check if source is file or directory
            var fileExists = await fileSystem.FileExistsAsync(source, cancellationToken);
            if (fileExists.IsSuccess && fileExists.Value)
            {
                // It's a file - use progress-aware copy
                return await CopyFileWithProgressAsync(
                    fileSystem, 
                    source, 
                    destination, 
                    options, 
                    cancellationToken);
            }
            
            var dirExists = await fileSystem.DirectoryExistsAsync(source, cancellationToken);
            if (dirExists.IsSuccess && dirExists.Value)
            {
                // It's a directory - use progress-aware copy
                return await CopyDirectoryWithProgressAsync(
                    fileSystem, 
                    source, 
                    destination, 
                    options, 
                    cancellationToken);
            }
            
            return Result<Unit, Error>.Failure(
                new Error(FileSystemErrorCodes.NotFound.Path, "Source path not found").With("path", source.Value));
        }
        
        /// <summary>
        /// Copies a file with progress reporting.
        /// </summary>
        private static async Task<Result<Unit, Error>> CopyFileWithProgressAsync(
            IFileSystem fileSystem,
            CanonicalPath source,
            CanonicalPath destination,
            CopyOptions options,
            CancellationToken cancellationToken)
        {
            var progressReporter = options.Progress != null 
                ? new ProgressReporter(options.Progress, TimeSpan.FromMilliseconds(100))
                : null;
            
            // Report start
            if (options.Progress != null)
            {
                options.Progress.Report(new CopyProgress(0, 0, source.Value, 0, 1));
            }
            
            // Perform the copy
            var result = await fileSystem.CopyFileAsync(
                source, 
                destination, 
                options.Overwrite, 
                cancellationToken);
            
            // Report completion
            if (result.IsSuccess && options.Progress != null)
            {
                options.Progress.Report(new CopyProgress(0, 0, destination.Value, 1, 1));
            }
            
            return result;
        }
        
        /// <summary>
        /// Copies a directory with progress reporting.
        /// </summary>
        private static async Task<Result<Unit, Error>> CopyDirectoryWithProgressAsync(
            IFileSystem fileSystem,
            CanonicalPath source,
            CanonicalPath destination,
            CopyOptions options,
            CancellationToken cancellationToken)
        {
            // For directory copy with progress, we need to enhance the built-in method
            // or provide our own implementation using traversal
            
            if (options.Progress == null)
            {
                // No progress needed, use built-in method
                return await fileSystem.CopyDirectoryAsync(source, destination, cancellationToken);
            }
            
            // With progress, we need custom implementation
            var handler = new ProgressCopyHandler(fileSystem, source, destination, options);
            var mapper = IdentityMapper.Instance;
            
            // Use traversal to collect files and calculate progress
            var traverseResult = await fileSystem.TraverseAsync(
                source, 
                handler, 
                mapper,
                cancellationToken: cancellationToken);
            
            if (traverseResult.IsFailure)
                return Result<Unit, Error>.Failure(traverseResult.Error);
            
            // Execute the copy operations
            return await handler.ExecuteCopyOperationsAsync(cancellationToken);
        }
    }
    
    /// <summary>
    /// Traversal handler that copies files with progress reporting.
    /// </summary>
    internal class ProgressCopyHandler : ITraversalHandler<FileSystemEntry>
    {
        private readonly IFileSystem _fileSystem;
        private readonly CanonicalPath _sourceRoot;
        private readonly CanonicalPath _destRoot;
        private readonly CopyOptions _options;
        private readonly List<(CanonicalPath source, CanonicalPath dest, long size)> _filesToCopy;
        private readonly ProgressReporter? _progressReporter;
        private long _totalBytes = 0;
        private long _bytesTransferred = 0;
        private int _filesProcessed = 0;
        
        public ProgressCopyHandler(
            IFileSystem fileSystem,
            CanonicalPath sourceRoot,
            CanonicalPath destRoot,
            CopyOptions options)
        {
            _fileSystem = fileSystem;
            _sourceRoot = sourceRoot;
            _destRoot = destRoot;
            _options = options;
            _filesToCopy = [];
            _progressReporter = options.Progress != null 
                ? new ProgressReporter(options.Progress, TimeSpan.FromMilliseconds(100))
                : null;
        }
        
        public Task<Result<Unit, Error>> OnTraversalStartAsync(
            CanonicalPath rootPath, 
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
        }
        
        public Task<Result<TraversalControl, Error>> OnFileAsync(
            FileSystemEntry file, 
            TraversalContext context, 
            CancellationToken cancellationToken = default)
        {
            var relativePath = GetRelativePath(_sourceRoot, file.Path);
            var destPath = CombinePaths(_destRoot, relativePath);
            var size = file.Size ?? 0;
            
            _filesToCopy.Add((file.Path, destPath, size));
            _totalBytes += size;
            
            return Task.FromResult(Result<TraversalControl, Error>.Success(TraversalControl.Continue));
        }
        
        public async Task<Result<TraversalControl, Error>> OnDirectoryEnterAsync(
            FileSystemEntry directory, 
            TraversalContext context, 
            CancellationToken cancellationToken = default)
        {
            if (context.CurrentDepth > 0)
            {
                var relativePath = GetRelativePath(_sourceRoot, directory.Path);
                var destPath = CombinePaths(_destRoot, relativePath);
                
                // Create directory
                var createResult = await _fileSystem.CreateDirectoryAsync(destPath, cancellationToken);
                if (createResult.IsFailure && !createResult.Error.Is(FileSystemErrorCodes.IO.DirectoryAlreadyExists))
                {
                    return Result<TraversalControl, Error>.Failure(createResult.Error);
                }
            }
            
            return Result<TraversalControl, Error>.Success(TraversalControl.Continue);
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
            return Task.FromResult(Result<Unit, Error>.Success(Unit.Value));
        }
        
        public async Task<Result<Unit, Error>> ExecuteCopyOperationsAsync(
            CancellationToken cancellationToken)
        {
            // Create destination root
            var createResult = await _fileSystem.CreateDirectoryAsync(_destRoot, cancellationToken);
            if (createResult.IsFailure && !createResult.Error.Is(FileSystemErrorCodes.IO.DirectoryAlreadyExists))
                return createResult;
            
            // Copy all files with progress
            foreach (var (source, dest, size) in _filesToCopy)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // Report progress
                _progressReporter?.ReportProgress(new CopyProgress(
                    _bytesTransferred,
                    _totalBytes,
                    source.Value,
                    _filesProcessed,
                    _filesToCopy.Count));
                
                // Copy file
                var copyResult = await _fileSystem.CopyFileAsync(
                    source, 
                    dest, 
                    _options.Overwrite, 
                    cancellationToken);
                
                if (copyResult.IsFailure)
                    return copyResult;
                
                _bytesTransferred += size;
                _filesProcessed++;
            }
            
            // Report completion
            _progressReporter?.ReportProgress(new CopyProgress(
                _totalBytes,
                _totalBytes,
                _destRoot.Value,
                _filesToCopy.Count,
                _filesToCopy.Count), force: true);
            
            return Result<Unit, Error>.Success(Unit.Value);
        }
        
        private string GetRelativePath(CanonicalPath basePath, CanonicalPath fullPath)
        {
            var baseValue = basePath.Value.TrimEnd('/') + "/";
            var fullValue = fullPath.Value;
            
            if (!fullValue.StartsWith(baseValue))
                return Path.GetFileName(fullValue);
            
            return fullValue.Substring(baseValue.Length);
        }
        
        private CanonicalPath CombinePaths(CanonicalPath basePath, string relativePath)
        {
            var combined = basePath.Value.TrimEnd('/') + "/" + relativePath;
            return CanonicalPath.Create(combined).Value;
        }
    }
}
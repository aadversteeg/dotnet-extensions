using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Implements the visitor pattern for file system traversal.
    /// </summary>
    public class FileSystemVisitor : IFileSystemVisitor
    {
        private readonly IFileSystem _fileSystem;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileSystemVisitor"/> class.
        /// </summary>
        /// <param name="fileSystem">The file system to use for traversal.</param>
        public FileSystemVisitor(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        /// <inheritdoc />
        public async Task<Result<FileSystemVisitResult, FileSystemError>> VisitAsync(
            CanonicalPath rootPath, 
            FileSystemVisitorOptions? options = null, 
            CancellationToken cancellationToken = default)
        {
            options ??= FileSystemVisitorOptions.Default;

            try
            {
                var initialResult = new FileSystemVisitResult(0, 0, 0, new List<FileSystemError>(), true);

                // Check if root path exists - no path conversion needed since we already have a CanonicalPath
                var existsResult = await _fileSystem.DirectoryExistsAsync(rootPath, cancellationToken);
                if (!existsResult.IsSuccess)
                    return Result<FileSystemVisitResult, FileSystemError>.Failure(existsResult.Error);

                if (!existsResult.Value)
                    return Result<FileSystemVisitResult, FileSystemError>.Failure(
                        new FileSystemError($"Directory does not exist: {rootPath}", rootPath.Value, FileSystemErrorType.DirectoryNotFound));

                var finalResult = await VisitDirectoryAsync(rootPath, rootPath, 0, options, initialResult, cancellationToken);

                return Result<FileSystemVisitResult, FileSystemError>.Success(finalResult);
            }
            catch (OperationCanceledException)
            {
                return Result<FileSystemVisitResult, FileSystemError>.Failure(
                    new FileSystemError("Operation was cancelled.", rootPath.Value, FileSystemErrorType.Unknown));
            }
            catch (Exception ex)
            {
                return Result<FileSystemVisitResult, FileSystemError>.Failure(
                    FileSystemError.FromException(ex, rootPath.Value));
            }
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<Result<FileVisitedEvent, FileSystemError>> VisitStreamAsync(
            CanonicalPath rootPath, 
            FileSystemVisitorOptions? options = null, 
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            options ??= FileSystemVisitorOptions.Default;

            // Check if root path exists - no path conversion needed since we already have a CanonicalPath
            var existsResult = await _fileSystem.DirectoryExistsAsync(rootPath, cancellationToken);
            if (!existsResult.IsSuccess)
            {
                yield return Result<FileVisitedEvent, FileSystemError>.Failure(existsResult.Error);
                yield break;
            }

            if (!existsResult.Value)
            {
                yield return Result<FileVisitedEvent, FileSystemError>.Failure(
                    new FileSystemError($"Directory does not exist: {rootPath}", rootPath.Value, FileSystemErrorType.DirectoryNotFound));
                yield break;
            }

            await foreach (var result in VisitDirectoryStreamAsync(rootPath, rootPath, 0, options, cancellationToken))
            {
                yield return result;
            }
        }

        private async Task<FileSystemVisitResult> VisitDirectoryAsync(
            CanonicalPath directoryPath, 
            CanonicalPath rootPath, 
            int depth, 
            FileSystemVisitorOptions options, 
            FileSystemVisitResult currentResult, 
            CancellationToken cancellationToken)
        {
            if (options.MaxDepth.HasValue && depth > options.MaxDepth.Value)
                return currentResult;

            try
            {
                // Get directory entry
                var dirEntryResult = await _fileSystem.GetEntryAsync(directoryPath, cancellationToken);
                if (!dirEntryResult.IsSuccess)
                {
                    var newErrors = new List<FileSystemError>(currentResult.Errors) { dirEntryResult.Error };
                    var errorResult = new FileSystemVisitResult(
                        currentResult.FilesVisited,
                        currentResult.DirectoriesVisited,
                        currentResult.TotalSize,
                        newErrors,
                        currentResult.IsCompleted);

                    if (options.OnError != null)
                    {
                        var action = options.OnError(dirEntryResult.Error);
                        if (action == FileSystemVisitorAction.Stop)
                        {
                            return new FileSystemVisitResult(
                                errorResult.FilesVisited,
                                errorResult.DirectoriesVisited,
                                errorResult.TotalSize,
                                errorResult.Errors,
                                false);
                        }
                    }
                    return errorResult;
                }

                var dirEntry = dirEntryResult.Value;

                // Check if we should visit this directory
                if (options.ShouldVisitDirectory != null && !options.ShouldVisitDirectory(dirEntry))
                    return currentResult;

                // Check hidden/system filters
                if (!options.IncludeHidden && dirEntry.IsHidden)
                    return currentResult;
                if (!options.IncludeSystem && dirEntry.IsSystem)
                    return currentResult;

                // Create result with incremented directory count
                var dirVisitedResult = new FileSystemVisitResult(
                    currentResult.FilesVisited,
                    currentResult.DirectoriesVisited + 1,
                    currentResult.TotalSize,
                    currentResult.Errors,
                    currentResult.IsCompleted);

                // Call directory visited callback
                if (options.OnDirectoryVisited != null)
                {
                    var action = options.OnDirectoryVisited(dirEntry);
                    if (action == FileSystemVisitorAction.Stop)
                    {
                        return new FileSystemVisitResult(
                            dirVisitedResult.FilesVisited,
                            dirVisitedResult.DirectoriesVisited,
                            dirVisitedResult.TotalSize,
                            dirVisitedResult.Errors,
                            false);
                    }
                    if (action == FileSystemVisitorAction.SkipSubtree)
                        return dirVisitedResult;
                }

                var accumulator = dirVisitedResult;

                // Process files in the directory
                var filesResult = await _fileSystem.GetFilesAsync(directoryPath, "*", SearchOption.TopDirectoryOnly, cancellationToken);
                if (filesResult.IsSuccess)
                {
                    foreach (var filePath in filesResult.Value)
                    {
                        accumulator = await VisitFileAsync(filePath, rootPath, depth, options, accumulator, cancellationToken);
                        if (!accumulator.IsCompleted)
                            return accumulator;
                    }
                }

                // Process subdirectories if recursion is enabled
                if (options.RecurseSubdirectories)
                {
                    var dirsResult = await _fileSystem.GetDirectoriesAsync(directoryPath, "*", SearchOption.TopDirectoryOnly, cancellationToken);
                    if (dirsResult.IsSuccess)
                    {
                        foreach (var subDirPath in dirsResult.Value)
                        {
                            accumulator = await VisitDirectoryAsync(subDirPath, rootPath, depth + 1, options, accumulator, cancellationToken);
                            if (!accumulator.IsCompleted)
                                return accumulator;
                        }
                    }
                }

                return accumulator;
            }
            catch (OperationCanceledException)
            {
                return new FileSystemVisitResult(
                    currentResult.FilesVisited,
                    currentResult.DirectoriesVisited,
                    currentResult.TotalSize,
                    currentResult.Errors,
                    false);
            }
            catch (Exception ex)
            {
                var error = FileSystemError.FromException(ex, directoryPath.Value);
                var newErrors = new List<FileSystemError>(currentResult.Errors) { error };
                var errorResult = new FileSystemVisitResult(
                    currentResult.FilesVisited,
                    currentResult.DirectoriesVisited,
                    currentResult.TotalSize,
                    newErrors,
                    currentResult.IsCompleted);

                if (options.OnError != null)
                {
                    var action = options.OnError(error);
                    if (action == FileSystemVisitorAction.Stop)
                    {
                        return new FileSystemVisitResult(
                            errorResult.FilesVisited,
                            errorResult.DirectoriesVisited,
                            errorResult.TotalSize,
                            errorResult.Errors,
                            false);
                    }
                }
                return errorResult;
            }
        }

        private async Task<FileSystemVisitResult> VisitFileAsync(
            CanonicalPath filePath, 
            CanonicalPath rootPath, 
            int depth, 
            FileSystemVisitorOptions options, 
            FileSystemVisitResult currentResult, 
            CancellationToken cancellationToken)
        {
            try
            {
                var fileEntryResult = await _fileSystem.GetEntryAsync(filePath, cancellationToken);
                if (!fileEntryResult.IsSuccess)
                {
                    var newErrors = new List<FileSystemError>(currentResult.Errors) { fileEntryResult.Error };
                    var errorResult = new FileSystemVisitResult(
                        currentResult.FilesVisited,
                        currentResult.DirectoriesVisited,
                        currentResult.TotalSize,
                        newErrors,
                        currentResult.IsCompleted);

                    if (options.OnError != null)
                    {
                        var action = options.OnError(fileEntryResult.Error);
                        if (action == FileSystemVisitorAction.Stop)
                        {
                            return new FileSystemVisitResult(
                                errorResult.FilesVisited,
                                errorResult.DirectoriesVisited,
                                errorResult.TotalSize,
                                errorResult.Errors,
                                false);
                        }
                    }
                    return errorResult;
                }

                var fileEntry = fileEntryResult.Value;

                // Check if we should visit this file
                if (options.ShouldVisitFile != null && !options.ShouldVisitFile(fileEntry))
                    return currentResult;

                // Check hidden/system filters
                if (!options.IncludeHidden && fileEntry.IsHidden)
                    return currentResult;
                if (!options.IncludeSystem && fileEntry.IsSystem)
                    return currentResult;

                // Create result with updated file count and size
                var fileVisitedResult = new FileSystemVisitResult(
                    currentResult.FilesVisited + 1,
                    currentResult.DirectoriesVisited,
                    currentResult.TotalSize + (fileEntry.Size ?? 0),
                    currentResult.Errors,
                    currentResult.IsCompleted);

                // Call file visited callback
                if (options.OnFileVisited != null)
                {
                    var action = options.OnFileVisited(fileEntry);
                    if (action == FileSystemVisitorAction.Stop)
                    {
                        return new FileSystemVisitResult(
                            fileVisitedResult.FilesVisited,
                            fileVisitedResult.DirectoriesVisited,
                            fileVisitedResult.TotalSize,
                            fileVisitedResult.Errors,
                            false);
                    }
                }

                return fileVisitedResult;
            }
            catch (Exception ex)
            {
                var error = FileSystemError.FromException(ex, filePath.Value);
                var newErrors = new List<FileSystemError>(currentResult.Errors) { error };
                var errorResult = new FileSystemVisitResult(
                    currentResult.FilesVisited,
                    currentResult.DirectoriesVisited,
                    currentResult.TotalSize,
                    newErrors,
                    currentResult.IsCompleted);

                if (options.OnError != null)
                {
                    var action = options.OnError(error);
                    if (action == FileSystemVisitorAction.Stop)
                    {
                        return new FileSystemVisitResult(
                            errorResult.FilesVisited,
                            errorResult.DirectoriesVisited,
                            errorResult.TotalSize,
                            errorResult.Errors,
                            false);
                    }
                }
                return errorResult;
            }
        }

        private async IAsyncEnumerable<Result<FileVisitedEvent, FileSystemError>> VisitDirectoryStreamAsync(
            CanonicalPath directoryPath,
            CanonicalPath rootPath,
            int depth,
            FileSystemVisitorOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (options.MaxDepth.HasValue && depth > options.MaxDepth.Value)
                yield break;

            // Get directory entry and yield directory visit event
            var dirEntryResult = await _fileSystem.GetEntryAsync(directoryPath, cancellationToken);
            if (!dirEntryResult.IsSuccess)
            {
                yield return Result<FileVisitedEvent, FileSystemError>.Failure(dirEntryResult.Error);
                yield break;
            }

            var dirEntry = dirEntryResult.Value;

            // Check filters
            if (!options.IncludeHidden && dirEntry.IsHidden)
                yield break;
            if (!options.IncludeSystem && dirEntry.IsSystem)
                yield break;
            if (options.ShouldVisitDirectory != null && !options.ShouldVisitDirectory(dirEntry))
                yield break;

            yield return Result<FileVisitedEvent, FileSystemError>.Success(
                new FileVisitedEvent(dirEntry, depth));

            // Process files
            var filesResult = await _fileSystem.GetFilesAsync(directoryPath, "*", SearchOption.TopDirectoryOnly, cancellationToken);
            if (filesResult.IsSuccess)
            {
                foreach (var filePath in filesResult.Value)
                {
                    var fileResult = await VisitFileStreamAsync(filePath, rootPath, depth, options, cancellationToken);
                    if (fileResult.HasValue)
                        yield return fileResult.Value;
                }
            }

            // Process subdirectories if recursion is enabled
            if (options.RecurseSubdirectories)
            {
                var dirsResult = await _fileSystem.GetDirectoriesAsync(directoryPath, "*", SearchOption.TopDirectoryOnly, cancellationToken);
                if (dirsResult.IsSuccess)
                {
                    foreach (var subDirPath in dirsResult.Value)
                    {
                        await foreach (var result in VisitDirectoryStreamAsync(subDirPath, rootPath, depth + 1, options, cancellationToken))
                        {
                            yield return result;
                        }
                    }
                }
            }
        }

        private async Task<Result<FileVisitedEvent, FileSystemError>?> VisitFileStreamAsync(
            CanonicalPath filePath,
            CanonicalPath rootPath,
            int depth,
            FileSystemVisitorOptions options,
            CancellationToken cancellationToken)
        {
            try
            {
                var fileEntryResult = await _fileSystem.GetEntryAsync(filePath, cancellationToken);
                if (!fileEntryResult.IsSuccess)
                    return Result<FileVisitedEvent, FileSystemError>.Failure(fileEntryResult.Error);

                var fileEntry = fileEntryResult.Value;

                // Check filters
                if (!options.IncludeHidden && fileEntry.IsHidden)
                    return null;
                if (!options.IncludeSystem && fileEntry.IsSystem)
                    return null;
                if (options.ShouldVisitFile != null && !options.ShouldVisitFile(fileEntry))
                    return null;

                return Result<FileVisitedEvent, FileSystemError>.Success(
                    new FileVisitedEvent(fileEntry, depth));
            }
            catch (Exception ex)
            {
                return Result<FileVisitedEvent, FileSystemError>.Failure(
                    FileSystemError.FromException(ex, filePath.Value));
            }
        }
    }
}
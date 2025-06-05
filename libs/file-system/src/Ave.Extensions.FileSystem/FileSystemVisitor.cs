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
        private readonly IPathUtilities _pathUtilities;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileSystemVisitor"/> class.
        /// </summary>
        /// <param name="fileSystem">The file system to use for traversal.</param>
        /// <param name="pathUtilities">The path utilities for path operations.</param>
        public FileSystemVisitor(IFileSystem fileSystem, IPathUtilities pathUtilities)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _pathUtilities = pathUtilities ?? throw new ArgumentNullException(nameof(pathUtilities));
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
                var result = new FileSystemVisitResult();

                // Check if root path exists - no path conversion needed since we already have a CanonicalPath
                var existsResult = await _fileSystem.DirectoryExistsAsync(rootPath, cancellationToken);
                if (!existsResult.IsSuccess)
                    return Result<FileSystemVisitResult, FileSystemError>.Failure(existsResult.Error);

                if (!existsResult.Value)
                    return Result<FileSystemVisitResult, FileSystemError>.Failure(
                        new FileSystemError($"Directory does not exist: {rootPath}", rootPath.Value, FileSystemErrorType.DirectoryNotFound));

                await VisitDirectoryAsync(rootPath, rootPath, 0, options, result, cancellationToken);

                return Result<FileSystemVisitResult, FileSystemError>.Success(result);
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
        public async IAsyncEnumerable<Result<FileVisitedEventArgs, FileSystemError>> VisitStreamAsync(
            CanonicalPath rootPath, 
            FileSystemVisitorOptions? options = null, 
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            options ??= FileSystemVisitorOptions.Default;

            // Check if root path exists - no path conversion needed since we already have a CanonicalPath
            var existsResult = await _fileSystem.DirectoryExistsAsync(rootPath, cancellationToken);
            if (!existsResult.IsSuccess)
            {
                yield return Result<FileVisitedEventArgs, FileSystemError>.Failure(existsResult.Error);
                yield break;
            }

            if (!existsResult.Value)
            {
                yield return Result<FileVisitedEventArgs, FileSystemError>.Failure(
                    new FileSystemError($"Directory does not exist: {rootPath}", rootPath.Value, FileSystemErrorType.DirectoryNotFound));
                yield break;
            }

            await foreach (var result in VisitDirectoryStreamAsync(rootPath, rootPath, 0, options, cancellationToken))
            {
                yield return result;
            }
        }

        private async Task VisitDirectoryAsync(
            CanonicalPath directoryPath, 
            CanonicalPath rootPath, 
            int depth, 
            FileSystemVisitorOptions options, 
            FileSystemVisitResult result, 
            CancellationToken cancellationToken)
        {
            if (options.MaxDepth.HasValue && depth > options.MaxDepth.Value)
                return;

            try
            {
                // Get directory entry
                var dirEntryResult = await _fileSystem.GetEntryAsync(directoryPath, cancellationToken);
                if (!dirEntryResult.IsSuccess)
                {
                    result.Errors.Add(dirEntryResult.Error);
                    if (options.OnError != null)
                    {
                        var action = options.OnError(dirEntryResult.Error);
                        if (action == FileSystemVisitorAction.Stop)
                        {
                            result.IsCompleted = false;
                            return;
                        }
                    }
                    return;
                }

                var dirEntry = dirEntryResult.Value;

                // Check if we should visit this directory
                if (options.ShouldVisitDirectory != null && !options.ShouldVisitDirectory(dirEntry))
                    return;

                // Check hidden/system filters
                if (!options.IncludeHidden && dirEntry.IsHidden)
                    return;
                if (!options.IncludeSystem && dirEntry.IsSystem)
                    return;

                result.DirectoriesVisited++;

                // Call directory visited callback
                if (options.OnDirectoryVisited != null)
                {
                    var action = options.OnDirectoryVisited(dirEntry);
                    if (action == FileSystemVisitorAction.Stop)
                    {
                        result.IsCompleted = false;
                        return;
                    }
                    if (action == FileSystemVisitorAction.SkipSubtree)
                        return;
                }

                // Process files in the directory
                var filesResult = await _fileSystem.GetFilesAsync(directoryPath, "*", SearchOption.TopDirectoryOnly, cancellationToken);
                if (filesResult.IsSuccess)
                {
                    foreach (var filePath in filesResult.Value)
                    {
                        await VisitFileAsync(filePath, rootPath, depth, options, result, cancellationToken);
                        if (!result.IsCompleted)
                            return;
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
                            await VisitDirectoryAsync(subDirPath, rootPath, depth + 1, options, result, cancellationToken);
                            if (!result.IsCompleted)
                                return;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                result.IsCompleted = false;
                throw;
            }
            catch (Exception ex)
            {
                var error = FileSystemError.FromException(ex, directoryPath.Value);
                result.Errors.Add(error);
                if (options.OnError != null)
                {
                    var action = options.OnError(error);
                    if (action == FileSystemVisitorAction.Stop)
                        result.IsCompleted = false;
                }
            }
        }

        private async Task VisitFileAsync(
            CanonicalPath filePath, 
            CanonicalPath rootPath, 
            int depth, 
            FileSystemVisitorOptions options, 
            FileSystemVisitResult result, 
            CancellationToken cancellationToken)
        {
            try
            {
                var fileEntryResult = await _fileSystem.GetEntryAsync(filePath, cancellationToken);
                if (!fileEntryResult.IsSuccess)
                {
                    result.Errors.Add(fileEntryResult.Error);
                    if (options.OnError != null)
                    {
                        var action = options.OnError(fileEntryResult.Error);
                        if (action == FileSystemVisitorAction.Stop)
                            result.IsCompleted = false;
                    }
                    return;
                }

                var fileEntry = fileEntryResult.Value;

                // Check if we should visit this file
                if (options.ShouldVisitFile != null && !options.ShouldVisitFile(fileEntry))
                    return;

                // Check hidden/system filters
                if (!options.IncludeHidden && fileEntry.IsHidden)
                    return;
                if (!options.IncludeSystem && fileEntry.IsSystem)
                    return;

                result.FilesVisited++;
                result.TotalSize += fileEntry.Size ?? 0;

                // Call file visited callback
                if (options.OnFileVisited != null)
                {
                    var action = options.OnFileVisited(fileEntry);
                    if (action == FileSystemVisitorAction.Stop)
                        result.IsCompleted = false;
                }
            }
            catch (Exception ex)
            {
                var error = FileSystemError.FromException(ex, filePath.Value);
                result.Errors.Add(error);
                if (options.OnError != null)
                {
                    var action = options.OnError(error);
                    if (action == FileSystemVisitorAction.Stop)
                        result.IsCompleted = false;
                }
            }
        }

        private async IAsyncEnumerable<Result<FileVisitedEventArgs, FileSystemError>> VisitDirectoryStreamAsync(
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
                yield return Result<FileVisitedEventArgs, FileSystemError>.Failure(dirEntryResult.Error);
                yield break;
            }

            var dirEntry = dirEntryResult.Value;
            var relativePath = _pathUtilities.GetRelativePath(rootPath.Value, directoryPath.Value);

            // Check filters
            if (!options.IncludeHidden && dirEntry.IsHidden)
                yield break;
            if (!options.IncludeSystem && dirEntry.IsSystem)
                yield break;
            if (options.ShouldVisitDirectory != null && !options.ShouldVisitDirectory(dirEntry))
                yield break;

            yield return Result<FileVisitedEventArgs, FileSystemError>.Success(
                new FileVisitedEventArgs(dirEntry, depth, relativePath));

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

        private async Task<Result<FileVisitedEventArgs, FileSystemError>?> VisitFileStreamAsync(
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
                    return Result<FileVisitedEventArgs, FileSystemError>.Failure(fileEntryResult.Error);

                var fileEntry = fileEntryResult.Value;

                // Check filters
                if (!options.IncludeHidden && fileEntry.IsHidden)
                    return null;
                if (!options.IncludeSystem && fileEntry.IsSystem)
                    return null;
                if (options.ShouldVisitFile != null && !options.ShouldVisitFile(fileEntry))
                    return null;

                var relativePath = _pathUtilities.GetRelativePath(rootPath.Value, filePath.Value);
                return Result<FileVisitedEventArgs, FileSystemError>.Success(
                    new FileVisitedEventArgs(fileEntry, depth, relativePath));
            }
            catch (Exception ex)
            {
                return Result<FileVisitedEventArgs, FileSystemError>.Failure(
                    FileSystemError.FromException(ex, filePath.Value));
            }
        }
    }
}
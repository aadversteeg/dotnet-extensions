using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.AzureFileStorage.Configuration;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Security;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ave.Extensions.FileSystem.AzureFileStorage
{
    /// <summary>y
    /// Azure File Share implementation of IFileSystem.
    /// </summary>
    public class AzureFileSystem : IFileSystem, IReadOnlyFileSystem
    {
        private readonly ILogger<AzureFileSystem> _logger;
        private readonly AzureFileSystemOptions _options;
        private readonly ShareClient _shareClient;
        private readonly IPathSecurity? _pathSecurity;
        
        /// <summary>
        /// Initializes a new instance of the AzureFileSystem class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="options">Azure file system options.</param>
        /// <param name="pathSecurity">Optional path security validator.</param>
        public AzureFileSystem(
            ILogger<AzureFileSystem> logger,
            IOptions<AzureFileSystemOptions> options,
            IPathSecurity? pathSecurity = null)
        {
            _logger = logger;
            _options = options.Value;
            _pathSecurity = pathSecurity;
            
            var serviceClient = new ShareServiceClient(_options.GetConnectionString());
            _shareClient = serviceClient.GetShareClient(_options.ShareName);
            
            if (_options.CreateShareIfNotExists)
            {
                _shareClient.CreateIfNotExists();
            }
        }

        /// <inheritdoc />
        public FileSystemInfo Info { get; } = new FileSystemInfo(
            isDurable: true,           // Azure File Share provides persistent storage
            supportsMonitoring: false, // Azure File Share does not support real-time monitoring
            isReadOnly: false          // Supports read and write operations
        );
        
        /// <inheritdoc />
        public async Task<Result<bool, FileSystemError>> FileExistsAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                var validationResult = ValidatePathSecurity(path);
                if (validationResult.IsFailure)
                    return Result<bool, FileSystemError>.Failure(validationResult.Error);
                
                var azurePath = GetAzurePath(path.Value);
                var fileClient = GetFileClient(azurePath);
                
                var response = await fileClient.ExistsAsync(cancellationToken);
                return Result<bool, FileSystemError>.Success(response.Value);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return Result<bool, FileSystemError>.Success(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check file existence in Azure: {Path}", path.Value);
                return Result<bool, FileSystemError>.Failure(
                    new FileSystemError($"Failed to check file existence: {ex.Message}", path.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<bool, FileSystemError>> DirectoryExistsAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                var validationResult = ValidatePathSecurity(path);
                if (validationResult.IsFailure)
                    return Result<bool, FileSystemError>.Failure(validationResult.Error);
                
                var azurePath = GetAzurePath(path.Value);
                var directoryClient = GetDirectoryClient(azurePath);
                
                var response = await directoryClient.ExistsAsync(cancellationToken);
                return Result<bool, FileSystemError>.Success(response.Value);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return Result<bool, FileSystemError>.Success(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check directory existence in Azure: {Path}", path.Value);
                return Result<bool, FileSystemError>.Failure(
                    new FileSystemError($"Failed to check directory existence: {ex.Message}", path.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<string, FileSystemError>> ReadAllTextAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                var validationResult = ValidatePathSecurity(path);
                if (validationResult.IsFailure)
                        return Result<string, FileSystemError>.Failure(validationResult.Error);
                
                var azurePath = GetAzurePath(path.Value);
                var fileClient = GetFileClient(azurePath);
                
                var response = await fileClient.DownloadAsync(cancellationToken: cancellationToken);
                using var reader = new StreamReader(response.Value.Content);
                var content = await reader.ReadToEndAsync();
                
                return Result<string, FileSystemError>.Success(content);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return Result<string, FileSystemError>.Failure(
                    new FileSystemError($"File not found: {path.Value}", path.Value, FileSystemErrorType.FileNotFound));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read file from Azure: {Path}", path.Value);
                return Result<string, FileSystemError>.Failure(
                    new FileSystemError($"Failed to read file: {ex.Message}", path.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Stream, FileSystemError>> OpenReadAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                var validationResult = ValidatePathSecurity(path);
                if (validationResult.IsFailure)
                        return Result<Stream, FileSystemError>.Failure(validationResult.Error);
                
                var azurePath = GetAzurePath(path.Value);
                var fileClient = GetFileClient(azurePath);
                
                var response = await fileClient.DownloadAsync(cancellationToken: cancellationToken);
                return Result<Stream, FileSystemError>.Success(response.Value.Content);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return Result<Stream, FileSystemError>.Failure(
                    new FileSystemError($"File not found: {path.Value}", path.Value, FileSystemErrorType.FileNotFound));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open file from Azure: {Path}", path.Value);
                return Result<Stream, FileSystemError>.Failure(
                    new FileSystemError($"Failed to open file: {ex.Message}", path.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<TEntry, FileSystemError>> GetEntryAsync<TEntry>(
            CanonicalPath path,
            IFileSystemEntryMapper<TEntry> mapper,
            CancellationToken cancellationToken = default)
        {
            if (mapper == null)
                throw new ArgumentNullException(nameof(mapper));
            
            try
            {
                var validationResult = ValidatePathSecurity(path);
                if (validationResult.IsFailure)
                    return Result<TEntry, FileSystemError>.Failure(validationResult.Error);
                
                var azurePath = GetAzurePath(path.Value);
                
                // Try as file first
                try
                {
                    var fileClient = GetFileClient(azurePath);
                    var fileResponse = await fileClient.GetPropertiesAsync(cancellationToken);
                    var fileProperties = fileResponse.Value;
                    
                    // Create EntryInfo
                    var entryInfo = new EntryInfo(
                        Path.GetFileName(path.Value),
                        fileProperties.SmbProperties.FileCreatedOn?.DateTime ?? DateTime.UtcNow,
                        fileProperties.LastModified.DateTime,
                        fileProperties.SmbProperties.FileLastWrittenOn?.DateTime ?? fileProperties.LastModified.DateTime,
                        ConvertSmbAttributes(fileProperties.SmbProperties.FileAttributes));
                    
                    // Create FileInfo
                    var fileInfo = new Models.FileInfo(entryInfo, fileProperties.ContentLength, Path.GetExtension(path.Value));
                    
                    // Create FileEntry
                    var fileEntry = new Models.FileEntry(path, fileInfo);
                    
                    // Create file reader for this specific file
                    var fileReader = new AzureFileReader(this, path);
                    return await mapper.MapFileAsync(fileEntry, fileReader, cancellationToken);
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    // Not a file, try as directory
                    try
                    {
                        var directoryClient = GetDirectoryClient(azurePath);
                        var dirResponse = await directoryClient.GetPropertiesAsync(cancellationToken);
                        var dirProperties = dirResponse.Value;
                        
                        // Create EntryInfo
                        var entryInfo = new EntryInfo(
                            Path.GetFileName(path.Value.TrimEnd('/')) ?? string.Empty,
                            dirProperties.SmbProperties.FileCreatedOn?.DateTime ?? DateTime.UtcNow,
                            dirProperties.LastModified.DateTime,
                            dirProperties.SmbProperties.FileLastWrittenOn?.DateTime ?? dirProperties.LastModified.DateTime,
                            ConvertSmbAttributes(dirProperties.SmbProperties.FileAttributes) | FileAttributes.Directory);
                        
                        // Create DirectoryInfo
                        var directoryInfo = new Models.DirectoryInfo(entryInfo);
                        
                        // Create DirectoryEntry
                        var directoryEntry = new Models.DirectoryEntry(path, directoryInfo);
                        return await mapper.MapDirectoryAsync(directoryEntry, cancellationToken);
                    }
                    catch (RequestFailedException dirEx) when (dirEx.Status == 404)
                    {
                        return Result<TEntry, FileSystemError>.Failure(
                            new FileSystemError($"Path not found: {path.Value}", path.Value, FileSystemErrorType.PathNotFound));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get entry from Azure: {Path}", path.Value);
                return Result<TEntry, FileSystemError>.Failure(
                    new FileSystemError($"Failed to get entry: {ex.Message}", path.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<TraversalSummary, FileSystemError>> TraverseAsync(
            CanonicalPath path,
            ITraversalHandler handler,
            TraversalOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var validationResult = ValidatePathSecurity(path);
                if (validationResult.IsFailure)
                    return Result<TraversalSummary, FileSystemError>.Failure(validationResult.Error);

                options ??= new TraversalOptions();
                
                var startResult = await handler.OnTraversalStartAsync(path, cancellationToken);
                if (startResult.IsFailure) 
                    return Result<TraversalSummary, FileSystemError>.Failure(startResult.Error);

                var startTime = DateTime.UtcNow;
                long filesProcessed = 0;
                long directoriesProcessed = 0;
                long totalBytesProcessed = 0;

                var traversalResult = await TraverseDirectoryRecursiveAsync(
                    path, 
                    handler, 
                    options, 
                    TraversalContext.CreateInitial(path),
                    cancellationToken);

                if (traversalResult.IsFailure)
                    return Result<TraversalSummary, FileSystemError>.Failure(traversalResult.Error);

                filesProcessed = traversalResult.Value.FilesProcessed;
                directoriesProcessed = traversalResult.Value.DirectoriesProcessed;
                totalBytesProcessed = traversalResult.Value.TotalBytesProcessed;

                var duration = DateTime.UtcNow - startTime;
                var summary = new TraversalSummary(filesProcessed, directoriesProcessed, totalBytesProcessed, duration, false);

                var completeResult = await handler.OnTraversalCompleteAsync(summary, cancellationToken);
                if (completeResult.IsFailure) 
                    return Result<TraversalSummary, FileSystemError>.Failure(completeResult.Error);

                return Result<TraversalSummary, FileSystemError>.Success(summary);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return Result<TraversalSummary, FileSystemError>.Failure(
                    new FileSystemError($"Directory not found: {path.Value}", path.Value, FileSystemErrorType.DirectoryNotFound));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to traverse Azure directory: {Path}", path.Value);
                return Result<TraversalSummary, FileSystemError>.Failure(
                    new FileSystemError($"Failed to traverse directory: {ex.Message}", path.Value));
            }
        }

        private async Task<Result<TraversalSummary, FileSystemError>> TraverseDirectoryRecursiveAsync(
            CanonicalPath directoryPath,
            ITraversalHandler handler,
            TraversalOptions options,
            TraversalContext context,
            CancellationToken cancellationToken)
        {
            long filesProcessed = 0;
            long directoriesProcessed = 0;
            long totalBytesProcessed = 0;

            try
            {
                var azurePath = GetAzurePath(directoryPath.Value);
                var directoryClient = GetDirectoryClient(azurePath);

                // Process current directory - notify enter
                if (context.CurrentDepth > 0 || directoryPath.Value != context.RootPath.Value)
                {
                    var directoryEntry = await CreateDirectoryEntryInternalAsync(directoryPath, directoryClient, cancellationToken);
                    if (directoryEntry.IsSuccess)
                    {
                        var enterResult = await handler.OnDirectoryEnterAsync(directoryEntry.Value, context, cancellationToken);
                        if (enterResult.IsFailure)
                            return Result<TraversalSummary, FileSystemError>.Failure(enterResult.Error);

                        if (enterResult.Value.Action == TraversalAction.Stop)
                            return Result<TraversalSummary, FileSystemError>.Success(new TraversalSummary(0, 1, 0, TimeSpan.Zero, true));

                        if (enterResult.Value.Action == TraversalAction.Skip)
                            return Result<TraversalSummary, FileSystemError>.Success(new TraversalSummary(0, 1, 0, TimeSpan.Zero, false));

                        // Also process the directory itself
                        var dirResult = await handler.OnDirectoryAsync(directoryEntry.Value, context, cancellationToken);
                        if (dirResult.IsFailure)
                            return Result<TraversalSummary, FileSystemError>.Failure(dirResult.Error);

                        if (dirResult.Value.Action == TraversalAction.Stop)
                            return Result<TraversalSummary, FileSystemError>.Success(new TraversalSummary(0, 1, 0, TimeSpan.Zero, true));

                        directoriesProcessed++;
                    }
                }

                // Process files and subdirectories
                await foreach (var item in directoryClient.GetFilesAndDirectoriesAsync(cancellationToken: cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var itemPath = CombinePath(directoryPath.Value, item.Name);

                    if (!item.IsDirectory)
                    {
                        // Handle file
                        var fileEntry = await CreateFileEntryInternalAsync(itemPath, item, cancellationToken);
                        if (fileEntry.IsSuccess)
                        {
                            var fileResult = await handler.OnFileAsync(fileEntry.Value, context, cancellationToken);
                            if (fileResult.IsFailure)
                                return Result<TraversalSummary, FileSystemError>.Failure(fileResult.Error);

                            if (fileResult.Value.Action == TraversalAction.Stop)
                            {
                                var stoppedSummary = new TraversalSummary(filesProcessed + 1, directoriesProcessed, totalBytesProcessed, TimeSpan.Zero, true);
                                return Result<TraversalSummary, FileSystemError>.Success(stoppedSummary);
                            }

                            filesProcessed++;
                            totalBytesProcessed += fileEntry.Value.Info.Size;
                        }
                    }
                    else
                    {
                        // Handle subdirectory
                        var directoryEntry = await CreateDirectoryEntryInternalAsync(itemPath, GetDirectoryClient(GetAzurePath(itemPath.Value)), cancellationToken);
                        if (directoryEntry.IsSuccess)
                        {
                            var nextContext = context.NextLevel(itemPath);
                            
                            // Notify enter
                            var enterResult = await handler.OnDirectoryEnterAsync(directoryEntry.Value, nextContext, cancellationToken);
                            if (enterResult.IsFailure)
                                return Result<TraversalSummary, FileSystemError>.Failure(enterResult.Error);

                            if (enterResult.Value.Action == TraversalAction.Stop)
                            {
                                var stoppedSummary = new TraversalSummary(filesProcessed, directoriesProcessed + 1, totalBytesProcessed, TimeSpan.Zero, true);
                                return Result<TraversalSummary, FileSystemError>.Success(stoppedSummary);
                            }

                            if (enterResult.Value.Action == TraversalAction.Skip)
                            {
                                directoriesProcessed++;
                                continue;
                            }
                            
                            // Process directory
                            var dirResult = await handler.OnDirectoryAsync(directoryEntry.Value, nextContext, cancellationToken);
                            if (dirResult.IsFailure)
                                return Result<TraversalSummary, FileSystemError>.Failure(dirResult.Error);

                            if (dirResult.Value.Action == TraversalAction.Stop)
                            {
                                var stoppedSummary = new TraversalSummary(filesProcessed, directoriesProcessed + 1, totalBytesProcessed, TimeSpan.Zero, true);
                                return Result<TraversalSummary, FileSystemError>.Success(stoppedSummary);
                            }

                            if (dirResult.Value.Action == TraversalAction.Skip)
                            {
                                directoriesProcessed++;
                                continue;
                            }
                        }
                        directoriesProcessed++;

                        // Handle subdirectory recursively if requested
                        if (options.Recursive && (options.MaxDepth == null || context.CurrentDepth < options.MaxDepth))
                        {
                            var nextContext = context.NextLevel(itemPath);
                            var subResult = await TraverseDirectoryRecursiveAsync(itemPath, handler, options, nextContext, cancellationToken);
                            
                            if (subResult.IsFailure)
                                return subResult;

                            filesProcessed += subResult.Value.FilesProcessed;
                            directoriesProcessed += subResult.Value.DirectoriesProcessed;
                            totalBytesProcessed += subResult.Value.TotalBytesProcessed;

                            if (subResult.Value.WasCancelled)
                            {
                                var cancelledSummary = new TraversalSummary(filesProcessed, directoriesProcessed, totalBytesProcessed, TimeSpan.Zero, true);
                                return Result<TraversalSummary, FileSystemError>.Success(cancelledSummary);
                            }
                        }
                    }
                }

                return Result<TraversalSummary, FileSystemError>.Success(new TraversalSummary(filesProcessed, directoriesProcessed, totalBytesProcessed, TimeSpan.Zero, false));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to traverse directory: {Path}", directoryPath.Value);
                return Result<TraversalSummary, FileSystemError>.Failure(
                    new FileSystemError($"Failed to traverse directory: {ex.Message}", directoryPath.Value));
            }
        }

        private async Task<Result<IFileEntry, FileSystemError>> CreateFileEntryInternalAsync(
            CanonicalPath filePath, 
            ShareFileItem fileItem, 
            CancellationToken cancellationToken)
        {
            try
            {
                var azurePath = GetAzurePath(filePath.Value);
                var fileClient = GetFileClient(azurePath);
                var properties = await fileClient.GetPropertiesAsync(cancellationToken);

                var extension = Path.GetExtension(fileItem.Name);
                if (string.IsNullOrEmpty(extension))
                    extension = null;

                // Create EntryInfo
                var entryInfo = new EntryInfo(
                    fileItem.Name,
                    properties.Value.SmbProperties?.FileCreatedOn?.UtcDateTime ?? DateTime.MinValue,
                    properties.Value.LastModified.UtcDateTime,
                    properties.Value.SmbProperties?.FileLastWrittenOn?.UtcDateTime ?? DateTime.MinValue,
                    ConvertSmbAttributes(properties.Value.SmbProperties?.FileAttributes));
                
                // Create FileInfo
                var fileInfo = new Models.FileInfo(entryInfo, properties.Value.ContentLength, extension);
                
                // Create FileEntry
                var fileEntry = new Models.FileEntry(filePath, fileInfo);

                return Result<IFileEntry, FileSystemError>.Success(fileEntry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create file entry: {Path}", filePath.Value);
                return Result<IFileEntry, FileSystemError>.Failure(
                    new FileSystemError($"Failed to get file properties: {ex.Message}", filePath.Value));
            }
        }

        private async Task<Result<IDirectoryEntry, FileSystemError>> CreateDirectoryEntryInternalAsync(
            CanonicalPath directoryPath, 
            ShareDirectoryClient directoryClient, 
            CancellationToken cancellationToken)
        {
            try
            {
                var properties = await directoryClient.GetPropertiesAsync(cancellationToken);

                // Create EntryInfo
                var entryInfo = new EntryInfo(
                    Path.GetFileName(directoryPath.Value.TrimEnd('/')) ?? string.Empty,
                    properties.Value.SmbProperties?.FileCreatedOn?.UtcDateTime ?? DateTime.MinValue,
                    properties.Value.LastModified.UtcDateTime,
                    properties.Value.SmbProperties?.FileLastWrittenOn?.UtcDateTime ?? DateTime.MinValue,
                    ConvertSmbAttributes(properties.Value.SmbProperties?.FileAttributes) | FileAttributes.Directory);
                
                // Create DirectoryInfo
                var directoryInfo = new Models.DirectoryInfo(entryInfo);
                
                // Create DirectoryEntry
                var directoryEntry = new Models.DirectoryEntry(directoryPath, directoryInfo);

                return Result<IDirectoryEntry, FileSystemError>.Success(directoryEntry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create directory entry: {Path}", directoryPath.Value);
                return Result<IDirectoryEntry, FileSystemError>.Failure(
                    new FileSystemError($"Failed to get directory properties: {ex.Message}", directoryPath.Value));
            }
        }

        /// <inheritdoc />
        public Task<Result<IMonitoringSession, FileSystemError>> CreateMonitoringSessionAsync(MonitoringOptions options, CancellationToken cancellationToken = default)
        {
            // Azure File Share doesn't support real-time monitoring
            var error = new FileSystemError(
                "File system monitoring is not supported for Azure File Share", 
                options?.Path.Value, 
                FileSystemErrorType.NotSupported);
            
            return Task.FromResult(Result<IMonitoringSession, FileSystemError>.Failure(error));
        }

        /// <inheritdoc />
        public async Task<Result<Unit, FileSystemError>> CreateDirectoryAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                var validationResult = ValidatePathSecurity(path);
                if (validationResult.IsFailure)
                        return Result<Unit, FileSystemError>.Failure(validationResult.Error);
                
                var azurePath = GetAzurePath(path.Value);
                
                var result = await CreateDirectoryRecursiveAsync(azurePath, cancellationToken);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create directory in Azure: {Path}", path.Value);
                return Result<Unit, FileSystemError>.Failure(
                    new FileSystemError($"Failed to create directory: {ex.Message}", path.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Unit, FileSystemError>> WriteAllTextAsync(CanonicalPath path, string contents, CancellationToken cancellationToken = default)
        {
            try
            {
                var validationResult = ValidatePathSecurity(path);
                if (validationResult.IsFailure)
                        return Result<Unit, FileSystemError>.Failure(validationResult.Error);
                
                var azurePath = GetAzurePath(path.Value);
                var fileClient = GetFileClient(azurePath);
                
                // Ensure parent directory exists
                var parentDir = Path.GetDirectoryName(azurePath);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    var parentDirResult = await CreateDirectoryRecursiveAsync(parentDir, cancellationToken);
                    if (parentDirResult.IsFailure)
                        return parentDirResult;
                }
                
                var contentBytes = System.Text.Encoding.UTF8.GetBytes(contents ?? string.Empty);
                
                if (contentBytes.Length == 0)
                {
                    // Create empty file
                    await fileClient.CreateAsync(0, cancellationToken: cancellationToken);
                }
                else
                {
                    using var stream = new MemoryStream(contentBytes);
                    await fileClient.CreateAsync(contentBytes.Length, cancellationToken: cancellationToken);
                    await fileClient.UploadAsync(stream, cancellationToken: cancellationToken);
                }
                
                return Result<Unit, FileSystemError>.Success(Unit.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write file to Azure: {Path}", path.Value);
                return Result<Unit, FileSystemError>.Failure(
                    new FileSystemError($"Failed to write file: {ex.Message}", path.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Stream, FileSystemError>> OpenWriteAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                var validationResult = ValidatePathSecurity(path);
                if (validationResult.IsFailure)
                        return Result<Stream, FileSystemError>.Failure(validationResult.Error);
                
                // Azure File Share doesn't support direct write streams
                // Return a memory stream that can be uploaded when disposed
                var azurePath = GetAzurePath(path.Value);
                var fileClient = GetFileClient(azurePath);
                
                // Ensure parent directory exists
                var parentDir = Path.GetDirectoryName(azurePath);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    var parentDirResult = await CreateDirectoryRecursiveAsync(parentDir, cancellationToken);
                    if (parentDirResult.IsFailure)
                        return Result<Stream, FileSystemError>.Failure(parentDirResult.Error);
                }
                
                var writeStream = new WriteStream(fileClient, _logger);
                return Result<Stream, FileSystemError>.Success(writeStream);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open write stream for Azure: {Path}", path.Value);
                return Result<Stream, FileSystemError>.Failure(
                    new FileSystemError($"Failed to open write stream: {ex.Message}", path.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Stream, FileSystemError>> OpenAsync(CanonicalPath path, FileMode mode, FileAccess access, FileShare share, CancellationToken cancellationToken = default)
        {
            // Map file mode and access to appropriate Azure operations
            if (access == FileAccess.Read)
            {
                return await OpenReadAsync(path, cancellationToken);
            }
            else if (access == FileAccess.Write || access == FileAccess.ReadWrite)
            {
                return await OpenWriteAsync(path, cancellationToken);
            }
            
            return Result<Stream, FileSystemError>.Failure(
                new FileSystemError($"Unsupported file access mode: {access}", path.Value, FileSystemErrorType.NotSupported));
        }

        /// <inheritdoc />
        public async Task<Result<Unit, FileSystemError>> DeleteFileAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                var validationResult = ValidatePathSecurity(path);
                if (validationResult.IsFailure)
                        return Result<Unit, FileSystemError>.Failure(validationResult.Error);
                
                var azurePath = GetAzurePath(path.Value);
                var fileClient = GetFileClient(azurePath);
                
                await fileClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
                return Result<Unit, FileSystemError>.Success(Unit.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete file from Azure: {Path}", path.Value);
                return Result<Unit, FileSystemError>.Failure(
                    new FileSystemError($"Failed to delete file: {ex.Message}", path.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Unit, FileSystemError>> DeleteDirectoryAsync(CanonicalPath path, bool recursive = false, CancellationToken cancellationToken = default)
        {
            try
            {
                var validationResult = ValidatePathSecurity(path);
                if (validationResult.IsFailure)
                        return Result<Unit, FileSystemError>.Failure(validationResult.Error);
                
                var azurePath = GetAzurePath(path.Value);
                var directoryClient = GetDirectoryClient(azurePath);
                
                if (recursive)
                {
                    // Delete all contents first
                    await foreach (var item in directoryClient.GetFilesAndDirectoriesAsync(cancellationToken: cancellationToken))
                    {
                        if (item.IsDirectory)
                        {
                            var subDirResult = await DeleteDirectoryAsync(CombinePath(path.Value, item.Name), true, cancellationToken);
                            if (subDirResult.IsFailure)
                                return subDirResult;
                        }
                        else
                        {
                            var fileResult = await DeleteFileAsync(CombinePath(path.Value, item.Name), cancellationToken);
                            if (fileResult.IsFailure)
                                return fileResult;
                        }
                    }
                }
                
                await directoryClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
                return Result<Unit, FileSystemError>.Success(Unit.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete directory from Azure: {Path}", path.Value);
                return Result<Unit, FileSystemError>.Failure(
                    new FileSystemError($"Failed to delete directory: {ex.Message}", path.Value));
            }
        }

        // Helper methods
        private async Task<Result<Unit, FileSystemError>> CreateDirectoryRecursiveAsync(string azurePath, CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrEmpty(azurePath))
                    return Result<Unit, FileSystemError>.Success(Unit.Value);

                var pathParts = azurePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var currentPath = "";

                for (int i = 0; i < pathParts.Length; i++)
                {
                    currentPath = string.IsNullOrEmpty(currentPath) ? pathParts[i] : $"{currentPath}/{pathParts[i]}";
                    var directoryClient = GetDirectoryClient(currentPath);
                    
                    var exists = await directoryClient.ExistsAsync(cancellationToken);
                    if (!exists.Value)
                    {
                        await directoryClient.CreateAsync(cancellationToken: cancellationToken);
                    }
                }

                return Result<Unit, FileSystemError>.Success(Unit.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create directory recursively in Azure: {Path}", azurePath);
                return Result<Unit, FileSystemError>.Failure(
                    new FileSystemError($"Failed to create directory recursively: {ex.Message}", azurePath));
            }
        }
        
        private Result<Unit, FileSystemError> ValidatePathSecurity(CanonicalPath path)
        {
            if (_pathSecurity != null)
            {
                return _pathSecurity.ValidatePathAccess(path);
            }
            
            return Result<Unit, FileSystemError>.Success(Unit.Value);
        }
        
        private string GetAzurePath(string path)
        {
            var normalized = path.Replace('\\', '/').TrimStart('/');
            
            if (!string.IsNullOrEmpty(_options.BaseDirectory))
            {
                var baseDir = _options.BaseDirectory.Replace('\\', '/').TrimEnd('/');
                normalized = $"{baseDir}/{normalized}";
            }
            
            return normalized;
        }
        
        private ShareFileClient GetFileClient(string azurePath)
        {
            var segments = azurePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            if (segments.Length == 0)
                throw new ArgumentException("Invalid file path");
            
            ShareDirectoryClient dirClient = _shareClient.GetRootDirectoryClient();
            
            // Navigate through directories
            for (int i = 0; i < segments.Length - 1; i++)
            {
                dirClient = dirClient.GetSubdirectoryClient(segments[i]);
            }
            
            // Get file client for the last segment
            return dirClient.GetFileClient(segments[segments.Length - 1]);
        }
        
        private ShareDirectoryClient GetDirectoryClient(string azurePath)
        {
            if (string.IsNullOrEmpty(azurePath))
                return _shareClient.GetRootDirectoryClient();
            
            var segments = azurePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            ShareDirectoryClient dirClient = _shareClient.GetRootDirectoryClient();
            
            foreach (var segment in segments)
            {
                dirClient = dirClient.GetSubdirectoryClient(segment);
            }
            
            return dirClient;
        }
        
        private static bool MatchesPattern(string name, string pattern)
        {
            if (pattern == "*") return true;
            
            // Simple wildcard matching
            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            
            return System.Text.RegularExpressions.Regex.IsMatch(name, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        
        private static CanonicalPath CombinePath(string basePath, string fileName)
        {
            var combinedPath = basePath.TrimEnd('/') + "/" + fileName;
            var result = CanonicalPath.Create(combinedPath);
            if (result.IsFailure)
                throw new InvalidOperationException($"Failed to create canonical path: {combinedPath}");
            return result.Value;
        }
        
        private static FileAttributes ConvertSmbAttributes(NtfsFileAttributes? smbAttributes)
        {
            if (!smbAttributes.HasValue)
                return FileAttributes.Normal;
            
            var attributes = FileAttributes.Normal;
            
            if (smbAttributes.Value.HasFlag(NtfsFileAttributes.Hidden))
                attributes |= FileAttributes.Hidden;
            if (smbAttributes.Value.HasFlag(NtfsFileAttributes.ReadOnly))
                attributes |= FileAttributes.ReadOnly;
            if (smbAttributes.Value.HasFlag(NtfsFileAttributes.System))
                attributes |= FileAttributes.System;
            if (smbAttributes.Value.HasFlag(NtfsFileAttributes.Directory))
                attributes |= FileAttributes.Directory;
            
            return attributes;
        }
        
    }

    /// <summary>
    /// File reader implementation for Azure file system.
    /// </summary>
    internal sealed class AzureFileReader : IFileReader
    {
        private readonly AzureFileSystem _fileSystem;
        private readonly CanonicalPath _filePath;

        public AzureFileReader(AzureFileSystem fileSystem, CanonicalPath filePath)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _filePath = filePath;
        }

        public async Task<Result<byte[], FileSystemError>> ReadBytesAsync(int maxBytes, CancellationToken cancellationToken = default)
        {
            try
            {
                var openResult = await _fileSystem.OpenReadAsync(_filePath, cancellationToken);
                if (openResult.IsFailure)
                    return Result<byte[], FileSystemError>.Failure(openResult.Error);

                using var stream = openResult.Value;
                var buffer = new byte[Math.Min(maxBytes, (int)stream.Length)];
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                
                if (bytesRead < buffer.Length)
                {
                    Array.Resize(ref buffer, bytesRead);
                }
                
                return Result<byte[], FileSystemError>.Success(buffer);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result<byte[], FileSystemError>.Failure(
                    new FileSystemError($"Failed to read file: {ex.Message}", _filePath.Value, FileSystemErrorType.IOException));
            }
        }
    }
}
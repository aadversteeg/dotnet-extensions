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
using Ave.Extensions.FileSystem.AzureFileStorage.Handlers;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Security;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.FileSystem.Traversal.Strategies;
using Ave.Extensions.Functional;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ave.Extensions.FileSystem.AzureFileStorage
{
    /// <summary>
    /// Azure File Share implementation of IFileSystem.
    /// </summary>
    public class AzureFileSystem : IFileSystem, IReadOnlyFileSystem
    {
        private readonly ILogger<AzureFileSystem> _logger;
        private readonly AzureFileSystemOptions _options;
        private readonly ShareClient _shareClient;
        private readonly IPathSecurity? _pathSecurity;
        private readonly IPathMapper _pathMapper;

        private static readonly char[] PathSeparator = ['/'];
        
        /// <summary>
        /// Initializes a new instance of the AzureFileSystem class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="options">Azure file system options.</param>
        /// <param name="pathMapper">Path mapper for canonical path support.</param>
        /// <param name="pathSecurity">Optional path security validator.</param>
        public AzureFileSystem(
            ILogger<AzureFileSystem> logger,
            IOptions<AzureFileSystemOptions> options,
            IPathMapper pathMapper,
            IPathSecurity? pathSecurity = null)
        {
            _logger = logger;
            _options = options.Value;
            _pathMapper = pathMapper ?? throw new ArgumentNullException(nameof(pathMapper));
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
        public async Task<Result<bool, Error>> FileExistsAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                var validationResult = ValidatePathSecurity(path);
                if (validationResult.IsFailure)
                    return Result<bool, Error>.Failure(validationResult.Error);
                
                var systemPathResult = _pathMapper.MapToSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Result<bool, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value));
                
                var azurePath = GetAzurePath(systemPathResult.Value);
                var fileClient = GetFileClient(azurePath);
                
                var response = await fileClient.ExistsAsync(cancellationToken);
                return Result<bool, Error>.Success(response.Value);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return Result<bool, Error>.Success(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check file existence in Azure: {Path}", path.Value);
                return Result<bool, Error>.Failure(
                    new Error(FileSystemErrorCodes.Unknown, $"Failed to check file existence: {ex.Message}").With("path", path.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<bool, Error>> DirectoryExistsAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                var validationResult = ValidatePathSecurity(path);
                if (validationResult.IsFailure)
                    return Result<bool, Error>.Failure(validationResult.Error);
                
                var systemPathResult = _pathMapper.MapToSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Result<bool, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value));
                
                var azurePath = GetAzurePath(systemPathResult.Value);
                var directoryClient = GetDirectoryClient(azurePath);
                
                var response = await directoryClient.ExistsAsync(cancellationToken);
                return Result<bool, Error>.Success(response.Value);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return Result<bool, Error>.Success(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check directory existence in Azure: {Path}", path.Value);
                return Result<bool, Error>.Failure(
                    new Error(FileSystemErrorCodes.Unknown, $"Failed to check directory existence: {ex.Message}").With("path", path.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<string, Error>> ReadAllTextAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                var validationResult = ValidatePathSecurity(path);
                if (validationResult.IsFailure)
                        return Result<string, Error>.Failure(validationResult.Error);
                
                var systemPathResult = _pathMapper.MapToSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Result<string, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value));
                
                var azurePath = GetAzurePath(systemPathResult.Value);
                var fileClient = GetFileClient(azurePath);
                
                var response = await fileClient.DownloadAsync(cancellationToken: cancellationToken);
                using var reader = new StreamReader(response.Value.Content);
                var content = await reader.ReadToEndAsync();
                
                return Result<string, Error>.Success(content);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return Result<string, Error>.Failure(
                    new Error(FileSystemErrorCodes.NotFound.File, $"File not found: {path.Value}").With("path", path.Value));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read file from Azure: {Path}", path.Value);
                return Result<string, Error>.Failure(
                    new Error(FileSystemErrorCodes.Unknown, $"Failed to read file: {ex.Message}").With("path", path.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Stream, Error>> OpenReadAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                var validationResult = ValidatePathSecurity(path);
                if (validationResult.IsFailure)
                        return Result<Stream, Error>.Failure(validationResult.Error);
                
                var systemPathResult = _pathMapper.MapToSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Result<Stream, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value));
                
                var azurePath = GetAzurePath(systemPathResult.Value);
                var fileClient = GetFileClient(azurePath);
                
                var response = await fileClient.DownloadAsync(cancellationToken: cancellationToken);
                return Result<Stream, Error>.Success(response.Value.Content);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return Result<Stream, Error>.Failure(
                    new Error(FileSystemErrorCodes.NotFound.File, $"File not found: {path.Value}").With("path", path.Value));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open file from Azure: {Path}", path.Value);
                return Result<Stream, Error>.Failure(
                    new Error(FileSystemErrorCodes.Unknown, $"Failed to open file: {ex.Message}").With("path", path.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<TEntry, Error>> GetEntryAsync<TEntry>(
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
                    return Result<TEntry, Error>.Failure(validationResult.Error);
                
                var systemPathResult = _pathMapper.MapToSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Result<TEntry, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value));
                
                var azurePath = GetAzurePath(systemPathResult.Value);
                
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
                        return Result<TEntry, Error>.Failure(
                            new Error(FileSystemErrorCodes.NotFound.Path, $"Path not found: {path.Value}").With("path", path.Value));
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
                return Result<TEntry, Error>.Failure(
                    new Error(FileSystemErrorCodes.Unknown, $"Failed to get entry: {ex.Message}").With("path", path.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<TraversalSummary, Error>> TraverseAsync<TEntry>(
            CanonicalPath path,
            ITraversalHandler<TEntry> handler,
            IFileSystemEntryMapper<TEntry> mapper,
            TraversalOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var context = TraversalContext.CreateInitial(path);
            var stats = new TraversalStats();
            var completionNotified = false;

            try
            {
                var validationResult = ValidatePathSecurity(path);
                if (validationResult.IsFailure)
                    return Result<TraversalSummary, Error>.Failure(validationResult.Error);

                options ??= new TraversalOptions();

                // Notify handler of traversal start
                var startResult = await handler.OnTraversalStartAsync(path, cancellationToken);
                if (startResult.IsFailure)
                    return Result<TraversalSummary, Error>.Failure(startResult.Error);

                // Get the starting entry as IFileSystemEntry
                var entryResult = await GetStartingEntryAsync(path, cancellationToken);
                if (entryResult.IsFailure)
                    return Result<TraversalSummary, Error>.Failure(entryResult.Error);

                // Select strategy based on hint
                var strategy = CreateAzureStrategy(options.StrategyHint);

                // Perform traversal
                var traversalResult = await strategy.TraverseAsync(
                    entryResult.Value,
                    handler,
                    mapper,
                    options,
                    context,
                    stats,
                    GetChildrenAsync,
                    cancellationToken);

                if (traversalResult.IsFailure)
                    return Result<TraversalSummary, Error>.Failure(traversalResult.Error);

                var summary = CreateSummary(context, stats, cancellationToken);

                // Notify handler of completion
                completionNotified = true;
                var completeResult = await handler.OnTraversalCompleteAsync(summary, cancellationToken);
                if (completeResult.IsFailure)
                    return Result<TraversalSummary, Error>.Failure(completeResult.Error);

                return Result<TraversalSummary, Error>.Success(summary);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return Result<TraversalSummary, Error>.Failure(
                    new Error(FileSystemErrorCodes.NotFound.Directory, $"Directory not found: {path.Value}").With("path", path.Value));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to traverse Azure directory: {Path}", path.Value);
                return Result<TraversalSummary, Error>.Failure(
                    FileSystemErrors.FromException(ex, path.Value));
            }
            finally
            {
                // Completion is a lifecycle contract, not a success callback. Streaming handlers
                // hold their consumer open until it fires, so every exit - a failed path check, a
                // failed start, a missing starting entry, a failed traversal, a thrown exception,
                // cancellation - has to run it or the consumer waits forever. CancellationToken.None
                // because a cancelled traversal is exactly the case that must still release it.
                if (!completionNotified)
                {
                    completionNotified = true;
                    await handler.OnTraversalCompleteAsync(
                        CreateSummary(context, stats, cancellationToken),
                        CancellationToken.None);
                }
            }
        }

        private static TraversalSummary CreateSummary(
            TraversalContext context,
            TraversalStats stats,
            CancellationToken cancellationToken) =>
            new TraversalSummary(
                stats.FilesProcessed,
                stats.DirectoriesProcessed,
                stats.TotalBytesProcessed,
                DateTime.UtcNow - context.StartTimeUtc,
                cancellationToken.IsCancellationRequested);

        /// <summary>
        /// Creates a traversal strategy based on the provided hint.
        /// </summary>
        /// <param name="hint">The strategy hint.</param>
        /// <returns>An appropriate traversal strategy.</returns>
        private ITraversalStrategy CreateAzureStrategy(TraversalStrategyHint hint)
        {
            return hint switch
            {
                TraversalStrategyHint.Parallel => new AzureParallelTraversalStrategy(this),
                TraversalStrategyHint.MemoryEfficient => new AzureSequentialTraversalStrategy(this),
                TraversalStrategyHint.Search => new AzureSequentialTraversalStrategy(this),
                _ => new AzureSequentialTraversalStrategy(this)
            };
        }

        /// <summary>
        /// Gets a single entry as IFileSystemEntry for traversal starting point.
        /// </summary>
        /// <param name="path">The path to get the entry for.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the entry or an error.</returns>
        private async Task<Result<IFileSystemEntry, Error>> GetStartingEntryAsync(
            CanonicalPath path, 
            CancellationToken cancellationToken)
        {
            try
            {
                var systemPathResult = _pathMapper.MapToSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Result<IFileSystemEntry, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value));
                
                var azurePath = GetAzurePath(systemPathResult.Value);
                
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
                    IFileSystemEntry entry = new Models.FileEntry(path, fileInfo);
                    return Result<IFileSystemEntry, Error>.Success(entry);
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
                        IFileSystemEntry entry = new Models.DirectoryEntry(path, directoryInfo);
                        return Result<IFileSystemEntry, Error>.Success(entry);
                    }
                    catch (RequestFailedException dirEx) when (dirEx.Status == 404)
                    {
                        return Result<IFileSystemEntry, Error>.Failure(
                            new Error(FileSystemErrorCodes.NotFound.Path, $"Path not found: {path.Value}").With("path", path.Value));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result<IFileSystemEntry, Error>.Failure(
                    new Error(FileSystemErrorCodes.Unknown, $"Failed to get entry: {ex.Message}").With("path", path.Value));
            }
        }

        /// <summary>
        /// Gets the children of a directory for traversal.
        /// </summary>
        /// <param name="path">The directory path.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A result containing the children or an error.</returns>
        private async Task<Result<IEnumerable<IFileSystemEntry>, Error>> GetChildrenAsync(
            CanonicalPath path, 
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var systemPathResult = _pathMapper.MapToSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Result<IEnumerable<IFileSystemEntry>, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value));
                
                var azurePath = GetAzurePath(systemPathResult.Value);
                var directoryClient = GetDirectoryClient(azurePath);

                List<IFileSystemEntry> entries = [];

                await foreach (var item in directoryClient.GetFilesAndDirectoriesAsync(cancellationToken: cancellationToken))
                {
                    var itemPath = CombinePath(path.Value, item.Name);

                    if (!item.IsDirectory)
                    {
                        // Create file entry
                        var fileResult = await CreateFileEntryInternalAsync(itemPath, item, cancellationToken);
                        if (fileResult.IsSuccess)
                        {
                            entries.Add(fileResult.Value);
                        }
                    }
                    else
                    {
                        // Create directory entry
                        var dirClient = GetDirectoryClient(GetAzurePath(itemPath.Value));
                        var directoryResult = await CreateDirectoryEntryInternalAsync(itemPath, dirClient, cancellationToken);
                        if (directoryResult.IsSuccess)
                        {
                            entries.Add(directoryResult.Value);
                        }
                    }
                }

                return Result<IEnumerable<IFileSystemEntry>, Error>.Success(entries);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result<IEnumerable<IFileSystemEntry>, Error>.Failure(
                    FileSystemErrors.FromException(ex, path.Value));
            }
        }

        private async Task<Result<IFileEntry, Error>> CreateFileEntryInternalAsync(
            CanonicalPath filePath, 
            ShareFileItem fileItem, 
            CancellationToken cancellationToken)
        {
            try
            {
                var extension = Path.GetExtension(fileItem.Name);
                if (string.IsNullOrEmpty(extension))
                    extension = null;

                // For basic directory listing, we need to get the file properties
                // This is required for the Azure SDK to work correctly
                var azurePath = GetAzurePath(filePath.Value);
                var fileClient = GetFileClient(azurePath);
                var properties = await fileClient.GetPropertiesAsync(cancellationToken);

                // Create EntryInfo
                var entryInfo = new EntryInfo(
                    fileItem.Name,
                    properties.Value.SmbProperties?.FileCreatedOn?.UtcDateTime ?? DateTime.UtcNow,
                    properties.Value.LastModified.UtcDateTime,
                    properties.Value.SmbProperties?.FileLastWrittenOn?.UtcDateTime ?? properties.Value.LastModified.UtcDateTime,
                    ConvertSmbAttributes(properties.Value.SmbProperties?.FileAttributes));
                
                // Create FileInfo
                var fileInfo = new Models.FileInfo(entryInfo, properties.Value.ContentLength, extension);
                
                // Create FileEntry
                var fileEntry = new Models.FileEntry(filePath, fileInfo);

                return Result<IFileEntry, Error>.Success(fileEntry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create file entry: {Path}", filePath.Value);
                return Result<IFileEntry, Error>.Failure(
                    new Error(FileSystemErrorCodes.Unknown, $"Failed to get file properties: {ex.Message}").With("path", filePath.Value));
            }
        }

        private async Task<Result<IDirectoryEntry, Error>> CreateDirectoryEntryInternalAsync(
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

                return Result<IDirectoryEntry, Error>.Success(directoryEntry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create directory entry: {Path}", directoryPath.Value);
                return Result<IDirectoryEntry, Error>.Failure(
                    new Error(FileSystemErrorCodes.Unknown, $"Failed to get directory properties: {ex.Message}").With("path", directoryPath.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<IReadOnlyList<CanonicalPath>, Error>> ListFilesAsync(
            CanonicalPath directory,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var systemPathResult = _pathMapper.MapToSystemPath(directory);
                if (systemPathResult.IsFailure)
                    return Result<IReadOnlyList<CanonicalPath>, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message)
                            .With("path", directory.Value));

                var azurePath = GetAzurePath(systemPathResult.Value);
                var directoryClient = GetDirectoryClient(azurePath);

                var files = new List<CanonicalPath>();
                await foreach (var item in directoryClient.GetFilesAndDirectoriesAsync(cancellationToken: cancellationToken))
                {
                    if (!item.IsDirectory)
                        files.Add(CombinePath(directory.Value, item.Name));
                }

                return Result<IReadOnlyList<CanonicalPath>, Error>.Success(files);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result<IReadOnlyList<CanonicalPath>, Error>.Failure(
                    FileSystemErrors.FromException(ex, directory.Value));
            }
        }

        /// <inheritdoc />
        public Task<Result<IMonitoringSession, Error>> CreateMonitoringSessionAsync(MonitoringOptions options, CancellationToken cancellationToken = default)
        {
            // Azure File Share doesn't support real-time monitoring
            var error = new Error(
                FileSystemErrorCodes.NotSupported,
                "File system monitoring is not supported for Azure File Share");
            if (options?.Path.Value != null)
            {
                error = error.With("path", options.Path.Value);
            }

            return Task.FromResult(Result<IMonitoringSession, Error>.Failure(error));
        }

        /// <inheritdoc />
        public async Task<Result<Unit, Error>> CreateDirectoryAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                var validationResult = ValidatePathSecurity(path);
                if (validationResult.IsFailure)
                        return Result<Unit, Error>.Failure(validationResult.Error);
                
                var systemPathResult = _pathMapper.MapToSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value));
                
                var azurePath = GetAzurePath(systemPathResult.Value);
                _logger.LogDebug("CreateDirectoryAsync: {CanonicalPath} -> Azure path: {AzurePath}", path.Value, azurePath);
                
                var result = await CreateDirectoryRecursiveAsync(azurePath, cancellationToken);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create directory in Azure: {Path}", path.Value);
                return Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.Unknown, $"Failed to create directory: {ex.Message}").With("path", path.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Unit, Error>> WriteAllTextAsync(CanonicalPath path, string contents, CancellationToken cancellationToken = default)
        {
            try
            {
                var validationResult = ValidatePathSecurity(path);
                if (validationResult.IsFailure)
                        return Result<Unit, Error>.Failure(validationResult.Error);
                
                var systemPathResult = _pathMapper.MapToSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value));
                
                var azurePath = GetAzurePath(systemPathResult.Value);
                _logger.LogDebug("WriteAllTextAsync: {CanonicalPath} -> Azure path: {AzurePath}", path.Value, azurePath);
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
                
                return Result<Unit, Error>.Success(Unit.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write file to Azure: {Path}", path.Value);
                return Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.Unknown, $"Failed to write file: {ex.Message}").With("path", path.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Stream, Error>> OpenWriteAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                var validationResult = ValidatePathSecurity(path);
                if (validationResult.IsFailure)
                        return Result<Stream, Error>.Failure(validationResult.Error);
                
                // Azure File Share doesn't support direct write streams
                // Return a memory stream that can be uploaded when disposed
                var systemPathResult = _pathMapper.MapToSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Result<Stream, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value));
                
                var azurePath = GetAzurePath(systemPathResult.Value);
                var fileClient = GetFileClient(azurePath);
                
                // Ensure parent directory exists
                var parentDir = Path.GetDirectoryName(azurePath);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    var parentDirResult = await CreateDirectoryRecursiveAsync(parentDir, cancellationToken);
                    if (parentDirResult.IsFailure)
                        return Result<Stream, Error>.Failure(parentDirResult.Error);
                }
                
                var writeStream = new WriteStream(fileClient, _logger);
                return Result<Stream, Error>.Success(writeStream);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open write stream for Azure: {Path}", path.Value);
                return Result<Stream, Error>.Failure(
                    new Error(FileSystemErrorCodes.Unknown, $"Failed to open write stream: {ex.Message}").With("path", path.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Stream, Error>> OpenAsync(CanonicalPath path, FileMode mode, FileAccess access, FileShare share, CancellationToken cancellationToken = default)
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
            
            return Result<Stream, Error>.Failure(
                new Error(FileSystemErrorCodes.NotSupported, $"Unsupported file access mode: {access}").With("path", path.Value));
        }

        /// <inheritdoc />
        public async Task<Result<Unit, Error>> DeleteFileAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                var validationResult = ValidatePathSecurity(path);
                if (validationResult.IsFailure)
                        return Result<Unit, Error>.Failure(validationResult.Error);
                
                var systemPathResult = _pathMapper.MapToSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value));
                
                var azurePath = GetAzurePath(systemPathResult.Value);
                var fileClient = GetFileClient(azurePath);
                
                await fileClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
                return Result<Unit, Error>.Success(Unit.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete file from Azure: {Path}", path.Value);
                return Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.Unknown, $"Failed to delete file: {ex.Message}").With("path", path.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Unit, Error>> DeleteDirectoryAsync(CanonicalPath path, bool recursive = false, CancellationToken cancellationToken = default)
        {
            try
            {
                var validationResult = ValidatePathSecurity(path);
                if (validationResult.IsFailure)
                        return Result<Unit, Error>.Failure(validationResult.Error);
                
                var systemPathResult = _pathMapper.MapToSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value));
                
                var azurePath = GetAzurePath(systemPathResult.Value);
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
                return Result<Unit, Error>.Success(Unit.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete directory from Azure: {Path}", path.Value);
                return Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.Unknown, $"Failed to delete directory: {ex.Message}").With("path", path.Value));
            }
        }

        // Helper methods
        private async Task<Result<Unit, Error>> CreateDirectoryRecursiveAsync(string azurePath, CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrEmpty(azurePath))
                    return Result<Unit, Error>.Success(Unit.Value);

                var pathParts = azurePath.Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries);
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

                return Result<Unit, Error>.Success(Unit.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create directory recursively in Azure: {Path}", azurePath);
                return Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.Unknown, $"Failed to create directory recursively: {ex.Message}").With("path", azurePath));
            }
        }
        
        private Result<Unit, Error> ValidatePathSecurity(CanonicalPath path)
        {
            if (_pathSecurity != null)
            {
                return _pathSecurity.ValidatePathAccess(path);
            }
            
            return Result<Unit, Error>.Success(Unit.Value);
        }
        
        /// <summary>
        /// Converts a system path to an Azure-compatible path format.
        /// </summary>
        /// <param name="systemPath">The system path to convert.</param>
        /// <returns>Azure-compatible path format.</returns>
        private string GetAzurePath(string systemPath)
        {
            var normalized = systemPath.Replace('\\', '/').TrimStart('/');
            
            var baseDirectory = _options.BaseDirectory;
            if (baseDirectory != null && baseDirectory.Length > 0)
            {
                var baseDir = baseDirectory.Replace('\\', '/').TrimEnd('/');
                normalized = $"{baseDir}/{normalized}";
            }
            
            return normalized;
        }
        
        private ShareFileClient GetFileClient(string azurePath)
        {
            var segments = azurePath.Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            
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
            
            var segments = azurePath.Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries);
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

        #region Move/Copy Helper Methods

        /// <summary>
        /// Gets a ShareFileClient for the specified path.
        /// </summary>
        private ShareFileClient GetShareFileClient(CanonicalPath path)
        {
            var azurePath = GetAzurePath(path.Value);
            return GetFileClient(azurePath);
        }

        /// <summary>
        /// Gets a ShareDirectoryClient for the parent directory of a file.
        /// </summary>
        private ShareDirectoryClient GetParentDirectoryClient(CanonicalPath filePath)
        {
            var parentResult = filePath.GetParent();
            if (parentResult.IsFailure)
                throw new ArgumentException("File must have a parent directory", nameof(filePath));
            
            var azurePath = GetAzurePath(parentResult.Value.Value);
            return GetDirectoryClient(azurePath);
        }

        /// <summary>
        /// Ensures the parent directory exists for a file path.
        /// </summary>
        private async Task<Result<Unit, Error>> EnsureParentDirectoryExistsAsync(
            CanonicalPath filePath,
            CancellationToken cancellationToken)
        {
            try
            {
                var parentResult = filePath.GetParent();
                if (parentResult.IsFailure)
                    return Result<Unit, Error>.Success(Unit.Value);
                
                var azurePath = GetAzurePath(parentResult.Value.Value);
                var result = await CreateDirectoryRecursiveAsync(azurePath, cancellationToken);
                return result;
            }
            catch (RequestFailedException ex)
            {
                return Result<Unit, Error>.Failure(
                    new Error(MapAzureErrorToErrorCode(ex), $"Failed to create parent directory: {ex.Message}")
                        .With("path", filePath.Value));
            }
            catch (Exception ex)
            {
                return Result<Unit, Error>.Failure(
                    FileSystemErrors.FromException(ex, filePath.Value));
            }
        }

        /// <summary>
        /// Maps Azure error codes to ErrorCode.
        /// </summary>
        private ErrorCode MapAzureErrorToErrorCode(RequestFailedException ex)
        {
            var errorCode = ex.ErrorCode;

            if (errorCode == "ShareNotFound")
                return FileSystemErrorCodes.NotFound.Directory;
            if (errorCode == "ShareAlreadyExists")
                return FileSystemErrorCodes.IO.DirectoryAlreadyExists;
            if (errorCode == "ResourceNotFound")
                return FileSystemErrorCodes.NotFound.File;
            if (errorCode == "ResourceAlreadyExists")
                return FileSystemErrorCodes.IO.FileAlreadyExists;
            if (errorCode == "ParentNotFound")
                return FileSystemErrorCodes.NotFound.Directory;
            if (errorCode == "AuthenticationFailed")
                return FileSystemErrorCodes.IO.AccessDenied;
            if (errorCode == "InsufficientAccountPermissions")
                return FileSystemErrorCodes.IO.AccessDenied;
            if (errorCode == "ShareQuotaExceeded" || errorCode == "InsufficientSpace")
                return FileSystemErrorCodes.IO.InsufficientSpace;
            if (errorCode == "ShareSnapshotInProgress")
                return FileSystemErrorCodes.IO.General;

            return FileSystemErrorCodes.IO.General;
        }

        /// <summary>
        /// Waits for an Azure copy operation to complete.
        /// </summary>
        private async Task<Result<Unit, Error>> WaitForCopyToCompleteAsync(
            ShareFileClient fileClient,
            string copyId,
            CancellationToken cancellationToken)
        {
            var startTime = DateTime.UtcNow;
            var timeout = TimeSpan.FromMinutes(5); // Reasonable timeout for file copy
            
            while (DateTime.UtcNow - startTime < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var properties = await fileClient.GetPropertiesAsync(cancellationToken: cancellationToken);
                
                var copyStatus = properties.Value.CopyStatus;
                if (copyStatus == CopyStatus.Success)
                {
                    // Copy completed successfully
                    return Result<Unit, Error>.Success(Unit.Value);
                }
                
                switch (copyStatus)
                {
                    case CopyStatus.Success:
                        return Result<Unit, Error>.Success(Unit.Value);
                        
                    case CopyStatus.Failed:
                        return Result<Unit, Error>.Failure(
                            new Error(
                                FileSystemErrorCodes.IO.General,
                                $"Copy operation failed: {properties.Value.CopyStatusDescription}")
                                .With("path", fileClient.Path));

                    case CopyStatus.Aborted:
                        return Result<Unit, Error>.Failure(
                            new Error(
                                FileSystemErrorCodes.IO.General,
                                "Copy operation was aborted")
                                .With("path", fileClient.Path));
                        
                    case CopyStatus.Pending:
                        // Still copying, wait a bit
                        await Task.Delay(500, cancellationToken);
                        break;
                }
            }
            
            // Timeout
            return Result<Unit, Error>.Failure(
                new Error(
                    FileSystemErrorCodes.IO.General,
                    "Copy operation timed out")
                    .With("path", fileClient.Path));
        }

        #endregion

        /// <inheritdoc />
        public async Task<Result<Unit, Error>> MoveFileAsync(
            CanonicalPath source, 
            CanonicalPath destination, 
            bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate path security
                var sourceValidation = ValidatePathSecurity(source);
                if (sourceValidation.IsFailure)
                    return sourceValidation;
                
                var destValidation = ValidatePathSecurity(destination);
                if (destValidation.IsFailure)
                    return destValidation;
                
                // Validate paths are different
                if (source.Value.Equals(destination.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, "Source and destination paths are the same").With("path", source.Value));
                }

                // Azure Files doesn't support direct move, so we copy then delete
                var copyResult = await CopyFileAsync(source, destination, overwrite, cancellationToken);
                if (copyResult.IsFailure)
                    return copyResult;
                
                // Delete the source file after successful copy
                var sourceFileClient = GetShareFileClient(source);
                
                try
                {
                    await sourceFileClient.DeleteAsync(cancellationToken: cancellationToken);
                    return Result<Unit, Error>.Success(Unit.Value);
                }
                catch (RequestFailedException ex) when (ex.ErrorCode == "ResourceNotFound")
                {
                    // Source already deleted (rare race condition), consider it success
                    return Result<Unit, Error>.Success(Unit.Value);
                }
                catch (RequestFailedException ex)
                {
                    // Failed to delete source after copy - try to clean up destination
                    try
                    {
                        var destFileClient = GetShareFileClient(destination);
                        await destFileClient.DeleteAsync(cancellationToken: cancellationToken);
                    }
                    catch
                    {
                        // Best effort cleanup
                    }
                    
                    return Result<Unit, Error>.Failure(
                        new Error(
                            MapAzureErrorToErrorCode(ex),
                            $"Failed to delete source file after copy: {ex.Message}")
                            .With("path", source.Value));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result<Unit, Error>.Failure(
                    FileSystemErrors.FromException(ex, source.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Unit, Error>> MoveDirectoryAsync(
            CanonicalPath source, 
            CanonicalPath destination,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate path security
                var sourceValidation = ValidatePathSecurity(source);
                if (sourceValidation.IsFailure)
                    return sourceValidation;
                
                var destValidation = ValidatePathSecurity(destination);
                if (destValidation.IsFailure)
                    return destValidation;
                
                // Validate paths are different
                if (source.Value.Equals(destination.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, "Source and destination paths are the same").With("path", source.Value));
                }

                // Check if destination is a subdirectory of source
                if (IsSubdirectoryOf(source, destination))
                {
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, "Cannot move directory to a subdirectory of itself").With("path", source.Value));
                }

                // In Azure Files, directories are virtual, so we check by trying to list contents
                var checkResult = await CheckDirectoryExistsAsync(source, cancellationToken);
                if (checkResult.IsFailure)
                    return Result<Unit, Error>.Failure(checkResult.Error);
                
                if (!checkResult.Value)
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.NotFound.Directory, "Source directory not found").With("path", source.Value));

                // Check if destination already exists
                var destExistsResult = await CheckDirectoryExistsAsync(destination, cancellationToken);
                if (destExistsResult.IsSuccess && destExistsResult.Value)
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.DirectoryAlreadyExists, "Destination directory already exists").With("path", destination.Value));

                // Azure doesn't support direct move, so copy then delete
                var copyResult = await CopyDirectoryAsync(source, destination, cancellationToken);
                if (copyResult.IsFailure)
                    return copyResult;
                
                // Delete source directory after successful copy
                var deleteHandler = new AzureDeleteHandler(this);
                var mapper = IdentityMapper.Instance;
                var deleteResult = await TraverseAsync(source, deleteHandler, mapper, cancellationToken: cancellationToken);
                
                if (deleteResult.IsFailure)
                {
                    // Try to clean up the copy since delete failed
                    var cleanupHandler = new AzureDeleteHandler(this);
                    await TraverseAsync(destination, cleanupHandler, mapper, cancellationToken: cancellationToken);
                    
                    return Result<Unit, Error>.Failure(
                        deleteResult.Error.Wrap(deleteResult.Error.Code, $"Failed to delete source directory after copy: {deleteResult.Error.Message}")
                            .With("path", source.Value));
                }
                
                return Result<Unit, Error>.Success(Unit.Value);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result<Unit, Error>.Failure(
                    FileSystemErrors.FromException(ex, source.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Unit, Error>> CopyFileAsync(
            CanonicalPath source, 
            CanonicalPath destination, 
            bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate path security
                var sourceValidation = ValidatePathSecurity(source);
                if (sourceValidation.IsFailure)
                    return sourceValidation;
                
                var destValidation = ValidatePathSecurity(destination);
                if (destValidation.IsFailure)
                    return destValidation;
                
                // Validate paths are different
                if (source.Value.Equals(destination.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, "Source and destination paths are the same").With("path", source.Value));
                }

                var sourceFileClient = GetShareFileClient(source);
                var destFileClient = GetShareFileClient(destination);
                
                // Check if source exists
                var sourceExistsResult = await sourceFileClient.ExistsAsync(cancellationToken);
                if (!sourceExistsResult.Value)
                {
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.NotFound.File, "Source file not found").With("path", source.Value));
                }

                // Check if destination exists and overwrite is false
                if (!overwrite)
                {
                    var destExistsResult = await destFileClient.ExistsAsync(cancellationToken);
                    if (destExistsResult.Value)
                    {
                        return Result<Unit, Error>.Failure(
                            new Error(FileSystemErrorCodes.IO.FileAlreadyExists, "Destination file already exists").With("path", destination.Value));
                    }
                }
                
                // Ensure destination directory exists
                var ensureDirResult = await EnsureParentDirectoryExistsAsync(destination, cancellationToken);
                if (ensureDirResult.IsFailure)
                    return ensureDirResult;
                
                // Get source file properties to determine size
                var propertiesResponse = await sourceFileClient.GetPropertiesAsync(cancellationToken: cancellationToken);
                var fileSize = propertiesResponse.Value.ContentLength;
                
                // Create destination file with the same size
                await destFileClient.CreateAsync(fileSize, cancellationToken: cancellationToken);
                
                // Start the copy operation
                // Azure Files supports server-side copy which is efficient
                var copyOperation = await destFileClient.StartCopyAsync(sourceFileClient.Uri, cancellationToken: cancellationToken);
                
                // Wait for copy to complete
                var waitResult = await WaitForCopyToCompleteAsync(destFileClient, copyOperation.Value.CopyId, cancellationToken);
                if (waitResult.IsFailure)
                {
                    // Clean up failed copy
                    try
                    {
                        await destFileClient.DeleteAsync(cancellationToken: cancellationToken);
                    }
                    catch
                    {
                        // Best effort cleanup
                    }
                    return waitResult;
                }
                
                // Copy metadata and properties (best effort)
                try
                {
                    var sourceProperties = await sourceFileClient.GetPropertiesAsync(cancellationToken: cancellationToken);
                    
                    // Set metadata on destination if available
                    if (sourceProperties.Value.Metadata != null && sourceProperties.Value.Metadata.Count > 0)
                    {
                        try
                        {
                            await destFileClient.SetMetadataAsync(sourceProperties.Value.Metadata, cancellationToken);
                        }
                        catch
                        {
                            // Best effort - metadata copy failed but don't fail the entire operation
                        }
                    }
                }
                catch
                {
                    // Best effort - don't fail the copy if metadata preservation fails
                }
                
                return Result<Unit, Error>.Success(Unit.Value);
            }
            catch (RequestFailedException ex) when (ex.ErrorCode == "ShareNotFound" ||
                                                    ex.ErrorCode == "ParentNotFound")
            {
                return Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.NotFound.Directory, "Share or directory not found").With("path", source.Value));
            }
            catch (RequestFailedException ex) when (ex.ErrorCode == "ResourceNotFound")
            {
                return Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.NotFound.File, "File not found").With("path", source.Value));
            }
            catch (RequestFailedException ex) when (ex.ErrorCode == "ShareQuotaExceeded")
            {
                return Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.IO.InsufficientSpace, "Insufficient space in share").With("path", destination.Value));
            }
            catch (RequestFailedException ex)
            {
                return Result<Unit, Error>.Failure(
                    new Error(MapAzureErrorToErrorCode(ex), $"Azure operation failed: {ex.Message}").With("path", source.Value));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result<Unit, Error>.Failure(
                    FileSystemErrors.FromException(ex, source.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Unit, Error>> CopyDirectoryAsync(
            CanonicalPath source, 
            CanonicalPath destination,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate path security
                var sourceValidation = ValidatePathSecurity(source);
                if (sourceValidation.IsFailure)
                    return sourceValidation;
                
                var destValidation = ValidatePathSecurity(destination);
                if (destValidation.IsFailure)
                    return destValidation;
                
                // Validate paths are different
                if (source.Value.Equals(destination.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, "Source and destination paths are the same").With("path", source.Value));
                }

                // Check if destination is a subdirectory of source
                if (IsSubdirectoryOf(source, destination))
                {
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, "Cannot copy directory to a subdirectory of itself").With("path", source.Value));
                }

                // Check if source exists
                var checkResult = await CheckDirectoryExistsAsync(source, cancellationToken);
                if (checkResult.IsFailure)
                    return Result<Unit, Error>.Failure(checkResult.Error);

                if (!checkResult.Value)
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.NotFound.Directory, "Source directory not found").With("path", source.Value));

                // Check if destination already exists
                var destExistsResult = await CheckDirectoryExistsAsync(destination, cancellationToken);
                if (destExistsResult.IsSuccess && destExistsResult.Value)
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.DirectoryAlreadyExists, "Destination directory already exists").With("path", destination.Value));
                
                // Use traversal with copy handler
                var copyHandler = new AzureCopyHandler(this, source, destination);
                var mapper = IdentityMapper.Instance;
                var traverseResult = await TraverseAsync(source, copyHandler, mapper, cancellationToken: cancellationToken);
                
                if (traverseResult.IsFailure)
                    return Result<Unit, Error>.Failure(traverseResult.Error);
                
                // Check if there were any errors during copy
                if (copyHandler.Errors.Count > 0)
                {
                    var errorMessage = $"Directory copy completed with {copyHandler.Errors.Count} errors";
                    return Result<Unit, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.PartialFailure, errorMessage).With("path", source.Value));
                }
                
                return Result<Unit, Error>.Success(Unit.Value);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result<Unit, Error>.Failure(
                    FileSystemErrors.FromException(ex, source.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Unit, Error>> MoveAsync(
            CanonicalPath source, 
            CanonicalPath destination, 
            bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate path security
                var sourceValidation = ValidatePathSecurity(source);
                if (sourceValidation.IsFailure)
                    return sourceValidation;
                
                var destValidation = ValidatePathSecurity(destination);
                if (destValidation.IsFailure)
                    return destValidation;
                
                // Try as file first
                var fileClient = GetShareFileClient(source);
                var fileExistsResponse = await fileClient.ExistsAsync(cancellationToken);
                
                if (fileExistsResponse.Value)
                {
                    // It's a file
                    return await MoveFileAsync(source, destination, overwrite, cancellationToken);
                }
                
                // Check if it's a directory
                var dirExistsResult = await CheckDirectoryExistsAsync(source, cancellationToken);
                if (dirExistsResult.IsSuccess && dirExistsResult.Value)
                {
                    // It's a directory - overwrite parameter doesn't apply
                    if (overwrite)
                    {
                        var destDirExistsResult = await CheckDirectoryExistsAsync(destination, cancellationToken);
                        if (destDirExistsResult.IsSuccess && destDirExistsResult.Value)
                        {
                            return Result<Unit, Error>.Failure(
                                new Error(
                                    FileSystemErrorCodes.IO.DirectoryAlreadyExists,
                                    "Cannot overwrite an existing directory")
                                    .With("path", destination.Value));
                        }
                    }
                    return await MoveDirectoryAsync(source, destination, cancellationToken);
                }

                // Path not found
                return Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.NotFound.Path, "Source path not found").With("path", source.Value));
            }
            catch (RequestFailedException ex) when (ex.ErrorCode == "ResourceNotFound")
            {
                return Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.NotFound.Path, "Source path not found").With("path", source.Value));
            }
            catch (Exception ex)
            {
                return Result<Unit, Error>.Failure(
                    FileSystemErrors.FromException(ex, source.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Unit, Error>> CopyAsync(
            CanonicalPath source, 
            CanonicalPath destination, 
            bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate path security
                var sourceValidation = ValidatePathSecurity(source);
                if (sourceValidation.IsFailure)
                    return sourceValidation;
                
                var destValidation = ValidatePathSecurity(destination);
                if (destValidation.IsFailure)
                    return destValidation;
                
                // Try as file first
                var fileClient = GetShareFileClient(source);
                var fileExistsResponse = await fileClient.ExistsAsync(cancellationToken);
                
                if (fileExistsResponse.Value)
                {
                    // It's a file
                    return await CopyFileAsync(source, destination, overwrite, cancellationToken);
                }
                
                // Check if it's a directory
                var dirExistsResult = await CheckDirectoryExistsAsync(source, cancellationToken);
                if (dirExistsResult.IsSuccess && dirExistsResult.Value)
                {
                    // It's a directory - overwrite parameter doesn't apply
                    if (overwrite)
                    {
                        var destDirExistsResult = await CheckDirectoryExistsAsync(destination, cancellationToken);
                        if (destDirExistsResult.IsSuccess && destDirExistsResult.Value)
                        {
                            return Result<Unit, Error>.Failure(
                                new Error(
                                    FileSystemErrorCodes.IO.DirectoryAlreadyExists,
                                    "Cannot overwrite an existing directory")
                                    .With("path", destination.Value));
                        }
                    }
                    return await CopyDirectoryAsync(source, destination, cancellationToken);
                }

                // Path not found
                return Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.NotFound.Path, "Source path not found").With("path", source.Value));
            }
            catch (RequestFailedException ex) when (ex.ErrorCode == "ResourceNotFound")
            {
                return Result<Unit, Error>.Failure(
                    new Error(FileSystemErrorCodes.NotFound.Path, "Source path not found").With("path", source.Value));
            }
            catch (Exception ex)
            {
                return Result<Unit, Error>.Failure(
                    FileSystemErrors.FromException(ex, source.Value));
            }
        }

        #region Directory Helper Methods

        /// <summary>
        /// Checks if a directory exists by attempting to list its contents.
        /// </summary>
        private async Task<Result<bool, Error>> CheckDirectoryExistsAsync(
            CanonicalPath path,
            CancellationToken cancellationToken)
        {
            try
            {
                var systemPathResult = _pathMapper.MapToSystemPath(path);
                if (systemPathResult.IsFailure)
                    return Result<bool, Error>.Failure(
                        new Error(FileSystemErrorCodes.IO.InvalidPath, systemPathResult.Error.Message).With("path", path.Value));
                
                var azurePath = GetAzurePath(systemPathResult.Value);
                var directoryClient = GetDirectoryClient(azurePath);
                
                // Try to get properties first
                try
                {
                    await directoryClient.GetPropertiesAsync(cancellationToken: cancellationToken);
                    return Result<bool, Error>.Success(true);
                }
                catch (RequestFailedException ex) when (ex.ErrorCode == "ResourceNotFound")
                {
                    // Directory might still exist as a virtual directory with files
                }
                
                // Check if there are any files or subdirectories in this path
                await foreach (var item in directoryClient.GetFilesAndDirectoriesAsync(cancellationToken: cancellationToken))
                {
                    // If we find any item, the directory exists
                    return Result<bool, Error>.Success(true);
                }
                
                return Result<bool, Error>.Success(false);
            }
            catch (Exception ex)
            {
                return Result<bool, Error>.Failure(
                    FileSystemErrors.FromException(ex, path.Value));
            }
        }

        /// <summary>
        /// Checks if one path is a subdirectory of another.
        /// </summary>
        private bool IsSubdirectoryOf(CanonicalPath parent, CanonicalPath child)
        {
            var parentValue = parent.Value.TrimEnd('/') + "/";
            var childValue = child.Value.TrimEnd('/') + "/";
            
            return childValue.StartsWith(parentValue, StringComparison.OrdinalIgnoreCase) &&
                   !childValue.Equals(parentValue, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
        
    }

    /// <summary>
    /// Sequential traversal strategy for Azure file system that can create AzureFileReader instances.
    /// </summary>
    internal sealed class AzureSequentialTraversalStrategy : SequentialTraversalStrategy
    {
        private readonly AzureFileSystem _fileSystem;

        public AzureSequentialTraversalStrategy(AzureFileSystem fileSystem)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        protected override IFileReader CreateFileReader(IFileEntry fileEntry)
        {
            return new AzureFileReader(_fileSystem, fileEntry.Path);
        }
    }

    /// <summary>
    /// Parallel traversal strategy for Azure file system that can create AzureFileReader instances.
    /// </summary>
    internal sealed class AzureParallelTraversalStrategy : ParallelTraversalStrategy
    {
        private readonly AzureFileSystem _fileSystem;

        public AzureParallelTraversalStrategy(AzureFileSystem fileSystem, int? maxConcurrency = null) 
            : base(maxConcurrency)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        protected override IFileReader CreateFileReader(IFileEntry fileEntry)
        {
            return new AzureFileReader(_fileSystem, fileEntry.Path);
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

        public async Task<Result<byte[], Error>> ReadBytesAsync(int maxBytes, CancellationToken cancellationToken = default)
        {
            try
            {
                var openResult = await _fileSystem.OpenReadAsync(_filePath, cancellationToken);
                if (openResult.IsFailure)
                    return Result<byte[], Error>.Failure(openResult.Error);

                using var stream = openResult.Value;
                var buffer = new byte[Math.Min(maxBytes, (int)stream.Length)];
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                
                if (bytesRead < buffer.Length)
                {
                    Array.Resize(ref buffer, bytesRead);
                }
                
                return Result<byte[], Error>.Success(buffer);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result<byte[], Error>.Failure(
                    new Error(FileSystemErrorCodes.IO.General, $"Failed to read file: {ex.Message}").With("path", _filePath.Value));
            }
        }
    }
}
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
using Ave.Extensions.FileSystem.AzureStorage.Configuration;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Security;
using Ave.Extensions.Functional;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ave.Extensions.FileSystem.AzureStorage
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
        public async Task<Result<bool, FileSystemError>> FileExistsAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path.Value, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<bool, FileSystemError>.Failure(validationResult.Error);
                }
                
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
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path.Value, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<bool, FileSystemError>.Failure(validationResult.Error);
                }
                
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
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path.Value, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<string, FileSystemError>.Failure(validationResult.Error);
                }
                
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
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path.Value, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<Stream, FileSystemError>.Failure(validationResult.Error);
                }
                
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
        public async Task<Result<FileSystemEntry, FileSystemError>> GetEntryAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path.Value, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<FileSystemEntry, FileSystemError>.Failure(validationResult.Error);
                }
                
                var azurePath = GetAzurePath(path.Value);
                
                // Try as file first
                try
                {
                    var fileClient = GetFileClient(azurePath);
                    var fileResponse = await fileClient.GetPropertiesAsync(cancellationToken);
                    var fileProperties = fileResponse.Value;
                    
                    var entry = new FileSystemEntry
                    {
                        Path = path,
                        IsDirectory = false,
                        Size = fileProperties.ContentLength,
                        Extension = Path.GetExtension(path.Value),
                        CreatedUtc = fileProperties.SmbProperties.FileCreatedOn?.DateTime ?? DateTime.UtcNow,
                        ModifiedUtc = fileProperties.LastModified.DateTime,
                        LastAccessUtc = fileProperties.SmbProperties.FileLastWrittenOn?.DateTime ?? fileProperties.LastModified.DateTime,
                        Attributes = ConvertSmbAttributes(fileProperties.SmbProperties.FileAttributes)
                    };
                    
                    return Result<FileSystemEntry, FileSystemError>.Success(entry);
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    // Not a file, try as directory
                    try
                    {
                        var directoryClient = GetDirectoryClient(azurePath);
                        var dirResponse = await directoryClient.GetPropertiesAsync(cancellationToken);
                        var dirProperties = dirResponse.Value;
                        
                        var entry = new FileSystemEntry
                        {
                            Path = path,
                            IsDirectory = true,
                            Size = null,
                            Extension = null,
                            CreatedUtc = dirProperties.SmbProperties.FileCreatedOn?.DateTime ?? DateTime.UtcNow,
                            ModifiedUtc = dirProperties.LastModified.DateTime,
                            LastAccessUtc = dirProperties.SmbProperties.FileLastWrittenOn?.DateTime ?? dirProperties.LastModified.DateTime,
                            Attributes = ConvertSmbAttributes(dirProperties.SmbProperties.FileAttributes) | FileAttributes.Directory
                        };
                        
                        return Result<FileSystemEntry, FileSystemError>.Success(entry);
                    }
                    catch (RequestFailedException dirEx) when (dirEx.Status == 404)
                    {
                        return Result<FileSystemEntry, FileSystemError>.Failure(
                            new FileSystemError($"Path not found: {path.Value}", path.Value, FileSystemErrorType.PathNotFound));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get entry from Azure: {Path}", path.Value);
                return Result<FileSystemEntry, FileSystemError>.Failure(
                    new FileSystemError($"Failed to get entry: {ex.Message}", path.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<IReadOnlyList<CanonicalPath>, FileSystemError>> GetFilesAsync(CanonicalPath path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path.Value, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<IReadOnlyList<CanonicalPath>, FileSystemError>.Failure(validationResult.Error);
                }
                
                var azurePath = GetAzurePath(path.Value);
                var directoryClient = GetDirectoryClient(azurePath);
                
                var files = new List<CanonicalPath>();
                await foreach (var item in directoryClient.GetFilesAndDirectoriesAsync(cancellationToken: cancellationToken))
                {
                    if (!item.IsDirectory && MatchesPattern(item.Name, searchPattern))
                    {
                        files.Add(CombinePath(path.Value, item.Name));
                    }
                }
                
                return Result<IReadOnlyList<CanonicalPath>, FileSystemError>.Success(files);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return Result<IReadOnlyList<CanonicalPath>, FileSystemError>.Failure(
                    new FileSystemError($"Directory not found: {path.Value}", path.Value, FileSystemErrorType.DirectoryNotFound));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get files from Azure: {Path}", path.Value);
                return Result<IReadOnlyList<CanonicalPath>, FileSystemError>.Failure(
                    new FileSystemError($"Failed to get files: {ex.Message}", path.Value));
            }
        }

        /// <inheritdoc />
        public async Task<Result<IReadOnlyList<CanonicalPath>, FileSystemError>> GetDirectoriesAsync(CanonicalPath path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path.Value, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<IReadOnlyList<CanonicalPath>, FileSystemError>.Failure(validationResult.Error);
                }
                
                var azurePath = GetAzurePath(path.Value);
                var directoryClient = GetDirectoryClient(azurePath);
                
                var directories = new List<CanonicalPath>();
                await foreach (var item in directoryClient.GetFilesAndDirectoriesAsync(cancellationToken: cancellationToken))
                {
                    if (item.IsDirectory && MatchesPattern(item.Name, searchPattern))
                    {
                        directories.Add(CombinePath(path.Value, item.Name));
                    }
                }
                
                return Result<IReadOnlyList<CanonicalPath>, FileSystemError>.Success(directories);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return Result<IReadOnlyList<CanonicalPath>, FileSystemError>.Failure(
                    new FileSystemError($"Directory not found: {path.Value}", path.Value, FileSystemErrorType.DirectoryNotFound));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get directories from Azure: {Path}", path.Value);
                return Result<IReadOnlyList<CanonicalPath>, FileSystemError>.Failure(
                    new FileSystemError($"Failed to get directories: {ex.Message}", path.Value));
            }
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<Result<FileSystemEntry, FileSystemError>> EnumerateFileSystemEntriesAsync(
            CanonicalPath path,
            string searchPattern = "*",
            Models.EnumerationOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_pathSecurity != null && _options.AllowedPaths.Any())
            {
                var validationResult = _pathSecurity.ValidatePathAccess(path.Value, _options.AllowedPaths);
                if (validationResult.IsFailure)
                {
                    yield return Result<FileSystemEntry, FileSystemError>.Failure(validationResult.Error);
                    yield break;
                }
            }
            
            options ??= Models.EnumerationOptions.Default;
            
            await foreach (var entry in EnumerateEntriesRecursive(path, searchPattern, options, 0, cancellationToken))
            {
                yield return entry;
            }
        }

        /// <inheritdoc />
        public Task<IFileSystemMonitor> CreateMonitorAsync(CancellationToken cancellationToken = default)
        {
            // Azure File Share doesn't support real-time monitoring
            // Return a no-op monitor or throw NotSupportedException
            throw new NotSupportedException("File system monitoring is not supported for Azure File Share");
        }

        /// <inheritdoc />
        public async Task<Result<Unit, FileSystemError>> CreateDirectoryAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path.Value, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<Unit, FileSystemError>.Failure(validationResult.Error);
                }
                
                var azurePath = GetAzurePath(path.Value);
                var directoryClient = GetDirectoryClient(azurePath);
                
                await directoryClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
                return Result<Unit, FileSystemError>.Success(Unit.Value);
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
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path.Value, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<Unit, FileSystemError>.Failure(validationResult.Error);
                }
                
                var azurePath = GetAzurePath(path.Value);
                var fileClient = GetFileClient(azurePath);
                
                // Ensure parent directory exists
                var parentDir = Path.GetDirectoryName(azurePath);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    var parentDirClient = GetDirectoryClient(parentDir);
                    await parentDirClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
                }
                
                var contentBytes = System.Text.Encoding.UTF8.GetBytes(contents ?? string.Empty);
                using var stream = new MemoryStream(contentBytes);
                
                await fileClient.CreateAsync(contentBytes.Length, cancellationToken: cancellationToken);
                await fileClient.UploadAsync(stream, cancellationToken: cancellationToken);
                
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
        public Task<Result<Stream, FileSystemError>> OpenWriteAsync(CanonicalPath path, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path.Value, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Task.FromResult(Result<Stream, FileSystemError>.Failure(validationResult.Error));
                }
                
                // Azure File Share doesn't support direct write streams
                // Return a memory stream that can be uploaded when disposed
                var azurePath = GetAzurePath(path.Value);
                var fileClient = GetFileClient(azurePath);
                
                var writeStream = new WriteStream(fileClient, _logger);
                return Task.FromResult(Result<Stream, FileSystemError>.Success(writeStream));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open write stream for Azure: {Path}", path.Value);
                return Task.FromResult(Result<Stream, FileSystemError>.Failure(
                    new FileSystemError($"Failed to open write stream: {ex.Message}", path.Value)));
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
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path.Value, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<Unit, FileSystemError>.Failure(validationResult.Error);
                }
                
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
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path.Value, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<Unit, FileSystemError>.Failure(validationResult.Error);
                }
                
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
        
        private async IAsyncEnumerable<Result<FileSystemEntry, FileSystemError>> EnumerateEntriesRecursive(
            CanonicalPath path,
            string searchPattern,
            Models.EnumerationOptions options,
            int currentDepth,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (options.MaxDepth.HasValue && currentDepth > options.MaxDepth.Value)
                yield break;
            
            var azurePath = GetAzurePath(path.Value);
            var directoryClient = GetDirectoryClient(azurePath);
            var items = directoryClient.GetFilesAndDirectoriesAsync(cancellationToken: cancellationToken);
            
            await foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var fullPath = CombinePath(path.Value, item.Name);
                
                if (item.IsDirectory)
                {
                    if (!options.IncludeHidden && item.Name.StartsWith('.'))
                        continue;
                    
                    if (options.ShouldIncludeDirectory != null && !options.ShouldIncludeDirectory(fullPath.Value))
                        continue;
                    
                    var entry = new FileSystemEntry
                    {
                        Path = fullPath,
                        IsDirectory = true,
                        Size = null,
                        Extension = null,
                        CreatedUtc = DateTime.UtcNow, // Azure doesn't provide this in listing
                        ModifiedUtc = DateTime.UtcNow, // Azure doesn't provide this in listing
                        LastAccessUtc = DateTime.UtcNow, // Azure doesn't provide this in listing
                        Attributes = FileAttributes.Directory
                    };
                    
                    yield return Result<FileSystemEntry, FileSystemError>.Success(entry);
                    
                    // Recurse if enabled
                    if (options.RecurseSubdirectories)
                    {
                        await foreach (var subEntry in EnumerateEntriesRecursive(fullPath, searchPattern, options, currentDepth + 1, cancellationToken))
                        {
                            yield return subEntry;
                        }
                    }
                }
                else
                {
                    if (!MatchesPattern(item.Name, searchPattern))
                        continue;
                    
                    if (!options.IncludeHidden && item.Name.StartsWith('.'))
                        continue;
                    
                    if (options.ShouldIncludeFile != null && !options.ShouldIncludeFile(fullPath.Value))
                        continue;
                    
                    var entry = new FileSystemEntry
                    {
                        Path = fullPath,
                        IsDirectory = false,
                        Size = null, // Size not available in listing, would need separate API call
                        Extension = Path.GetExtension(item.Name),
                        CreatedUtc = DateTime.UtcNow, // Azure doesn't provide this in listing
                        ModifiedUtc = DateTime.UtcNow, // Azure doesn't provide this in listing  
                        LastAccessUtc = DateTime.UtcNow, // Azure doesn't provide this in listing
                        Attributes = FileAttributes.Normal
                    };
                    
                    yield return Result<FileSystemEntry, FileSystemError>.Success(entry);
                }
            }
        }
    }
}
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
    /// <summary>
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
        public async Task<Result<bool, FileSystemError>> FileExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<bool, FileSystemError>.Failure(validationResult.Error);
                }
                
                var azurePath = GetAzurePath(path);
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
                _logger.LogError(ex, "Failed to check file existence in Azure: {Path}", path);
                return Result<bool, FileSystemError>.Failure(
                    new FileSystemError($"Failed to check file existence: {ex.Message}", path));
            }
        }

        /// <inheritdoc />
        public async Task<Result<bool, FileSystemError>> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<bool, FileSystemError>.Failure(validationResult.Error);
                }
                
                var azurePath = GetAzurePath(path);
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
                _logger.LogError(ex, "Failed to check directory existence in Azure: {Path}", path);
                return Result<bool, FileSystemError>.Failure(
                    new FileSystemError($"Failed to check directory existence: {ex.Message}", path));
            }
        }

        /// <inheritdoc />
        public async Task<Result<string, FileSystemError>> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<string, FileSystemError>.Failure(validationResult.Error);
                }
                
                var azurePath = GetAzurePath(path);
                var fileClient = GetFileClient(azurePath);
                
                var response = await fileClient.DownloadAsync(cancellationToken: cancellationToken);
                using var reader = new StreamReader(response.Value.Content);
                var content = await reader.ReadToEndAsync();
                
                return Result<string, FileSystemError>.Success(content);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return Result<string, FileSystemError>.Failure(
                    new FileSystemError($"File not found: {path}", path, FileSystemErrorType.FileNotFound));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read file from Azure: {Path}", path);
                return Result<string, FileSystemError>.Failure(
                    new FileSystemError($"Failed to read file: {ex.Message}", path));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Stream, FileSystemError>> OpenReadAsync(string path, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<Stream, FileSystemError>.Failure(validationResult.Error);
                }
                
                var azurePath = GetAzurePath(path);
                var fileClient = GetFileClient(azurePath);
                
                var response = await fileClient.DownloadAsync(cancellationToken: cancellationToken);
                return Result<Stream, FileSystemError>.Success(response.Value.Content);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return Result<Stream, FileSystemError>.Failure(
                    new FileSystemError($"File not found: {path}", path, FileSystemErrorType.FileNotFound));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open file from Azure: {Path}", path);
                return Result<Stream, FileSystemError>.Failure(
                    new FileSystemError($"Failed to open file: {ex.Message}", path));
            }
        }

        /// <inheritdoc />
        public async Task<Result<FileMetadata, FileSystemError>> GetFileMetadataAsync(string path, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<FileMetadata, FileSystemError>.Failure(validationResult.Error);
                }
                
                var azurePath = GetAzurePath(path);
                var fileClient = GetFileClient(azurePath);
                
                var response = await fileClient.GetPropertiesAsync(cancellationToken);
                var properties = response.Value;
                
                var metadata = new FileMetadata
                {
                    FullPath = path,
                    Name = Path.GetFileName(path),
                    Size = properties.ContentLength,
                    CreatedUtc = properties.SmbProperties.FileCreatedOn?.DateTime ?? DateTime.UtcNow,
                    ModifiedUtc = properties.LastModified.DateTime,
                    LastAccessUtc = properties.SmbProperties.FileLastWrittenOn?.DateTime ?? properties.LastModified.DateTime,
                    Attributes = ConvertSmbAttributes(properties.SmbProperties.FileAttributes)
                };
                
                return Result<FileMetadata, FileSystemError>.Success(metadata);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return Result<FileMetadata, FileSystemError>.Failure(
                    new FileSystemError($"File not found: {path}", path, FileSystemErrorType.FileNotFound));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get file metadata from Azure: {Path}", path);
                return Result<FileMetadata, FileSystemError>.Failure(
                    new FileSystemError($"Failed to get file metadata: {ex.Message}", path));
            }
        }

        /// <inheritdoc />
        public async Task<Result<DirectoryMetadata, FileSystemError>> GetDirectoryMetadataAsync(string path, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<DirectoryMetadata, FileSystemError>.Failure(validationResult.Error);
                }
                
                var azurePath = GetAzurePath(path);
                var directoryClient = GetDirectoryClient(azurePath);
                
                var response = await directoryClient.GetPropertiesAsync(cancellationToken);
                var properties = response.Value;
                
                var metadata = new DirectoryMetadata
                {
                    FullPath = path,
                    Name = Path.GetFileName(path.TrimEnd('/')),
                    CreatedUtc = properties.SmbProperties.FileCreatedOn?.DateTime ?? DateTime.UtcNow,
                    ModifiedUtc = properties.LastModified.DateTime,
                    LastAccessUtc = properties.SmbProperties.FileLastWrittenOn?.DateTime ?? properties.LastModified.DateTime,
                    Attributes = ConvertSmbAttributes(properties.SmbProperties.FileAttributes)
                };
                
                return Result<DirectoryMetadata, FileSystemError>.Success(metadata);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return Result<DirectoryMetadata, FileSystemError>.Failure(
                    new FileSystemError($"Directory not found: {path}", path, FileSystemErrorType.DirectoryNotFound));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get directory metadata from Azure: {Path}", path);
                return Result<DirectoryMetadata, FileSystemError>.Failure(
                    new FileSystemError($"Failed to get directory metadata: {ex.Message}", path));
            }
        }

        /// <inheritdoc />
        public async Task<Result<string[], FileSystemError>> GetFilesAsync(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<string[], FileSystemError>.Failure(validationResult.Error);
                }
                
                var azurePath = GetAzurePath(path);
                var directoryClient = GetDirectoryClient(azurePath);
                
                var files = new List<string>();
                await foreach (var item in directoryClient.GetFilesAndDirectoriesAsync(cancellationToken: cancellationToken))
                {
                    if (!item.IsDirectory && MatchesPattern(item.Name, searchPattern))
                    {
                        files.Add(CombinePath(path, item.Name));
                    }
                }
                
                return Result<string[], FileSystemError>.Success(files.ToArray());
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return Result<string[], FileSystemError>.Failure(
                    new FileSystemError($"Directory not found: {path}", path, FileSystemErrorType.DirectoryNotFound));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get files from Azure: {Path}", path);
                return Result<string[], FileSystemError>.Failure(
                    new FileSystemError($"Failed to get files: {ex.Message}", path));
            }
        }

        /// <inheritdoc />
        public async Task<Result<string[], FileSystemError>> GetDirectoriesAsync(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<string[], FileSystemError>.Failure(validationResult.Error);
                }
                
                var azurePath = GetAzurePath(path);
                var directoryClient = GetDirectoryClient(azurePath);
                
                var directories = new List<string>();
                await foreach (var item in directoryClient.GetFilesAndDirectoriesAsync(cancellationToken: cancellationToken))
                {
                    if (item.IsDirectory && MatchesPattern(item.Name, searchPattern))
                    {
                        directories.Add(CombinePath(path, item.Name));
                    }
                }
                
                return Result<string[], FileSystemError>.Success(directories.ToArray());
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return Result<string[], FileSystemError>.Failure(
                    new FileSystemError($"Directory not found: {path}", path, FileSystemErrorType.DirectoryNotFound));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get directories from Azure: {Path}", path);
                return Result<string[], FileSystemError>.Failure(
                    new FileSystemError($"Failed to get directories: {ex.Message}", path));
            }
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<Result<FileSystemEntry, FileSystemError>> EnumerateFileSystemEntriesAsync(
            string path,
            string searchPattern = "*",
            Models.EnumerationOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_pathSecurity != null && _options.AllowedPaths.Any())
            {
                var validationResult = _pathSecurity.ValidatePathAccess(path, _options.AllowedPaths);
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
        public async Task<Result<Unit, FileSystemError>> CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<Unit, FileSystemError>.Failure(validationResult.Error);
                }
                
                var azurePath = GetAzurePath(path);
                var directoryClient = GetDirectoryClient(azurePath);
                
                await directoryClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
                return Result<Unit, FileSystemError>.Success(Unit.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create directory in Azure: {Path}", path);
                return Result<Unit, FileSystemError>.Failure(
                    new FileSystemError($"Failed to create directory: {ex.Message}", path));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Unit, FileSystemError>> WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<Unit, FileSystemError>.Failure(validationResult.Error);
                }
                
                var azurePath = GetAzurePath(path);
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
                _logger.LogError(ex, "Failed to write file to Azure: {Path}", path);
                return Result<Unit, FileSystemError>.Failure(
                    new FileSystemError($"Failed to write file: {ex.Message}", path));
            }
        }

        /// <inheritdoc />
        public Task<Result<Stream, FileSystemError>> OpenWriteAsync(string path, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Task.FromResult(Result<Stream, FileSystemError>.Failure(validationResult.Error));
                }
                
                // Azure File Share doesn't support direct write streams
                // Return a memory stream that can be uploaded when disposed
                var azurePath = GetAzurePath(path);
                var fileClient = GetFileClient(azurePath);
                
                var writeStream = new WriteStream(fileClient, _logger);
                return Task.FromResult(Result<Stream, FileSystemError>.Success(writeStream));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open write stream for Azure: {Path}", path);
                return Task.FromResult(Result<Stream, FileSystemError>.Failure(
                    new FileSystemError($"Failed to open write stream: {ex.Message}", path)));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Stream, FileSystemError>> OpenAsync(string path, FileMode mode, FileAccess access, FileShare share, CancellationToken cancellationToken = default)
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
                new FileSystemError($"Unsupported file access mode: {access}", path, FileSystemErrorType.NotSupported));
        }

        /// <inheritdoc />
        public async Task<Result<Unit, FileSystemError>> DeleteFileAsync(string path, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<Unit, FileSystemError>.Failure(validationResult.Error);
                }
                
                var azurePath = GetAzurePath(path);
                var fileClient = GetFileClient(azurePath);
                
                await fileClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
                return Result<Unit, FileSystemError>.Success(Unit.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete file from Azure: {Path}", path);
                return Result<Unit, FileSystemError>.Failure(
                    new FileSystemError($"Failed to delete file: {ex.Message}", path));
            }
        }

        /// <inheritdoc />
        public async Task<Result<Unit, FileSystemError>> DeleteDirectoryAsync(string path, bool recursive = false, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_pathSecurity != null && _options.AllowedPaths.Any())
                {
                    var validationResult = _pathSecurity.ValidatePathAccess(path, _options.AllowedPaths);
                    if (validationResult.IsFailure)
                        return Result<Unit, FileSystemError>.Failure(validationResult.Error);
                }
                
                var azurePath = GetAzurePath(path);
                var directoryClient = GetDirectoryClient(azurePath);
                
                if (recursive)
                {
                    // Delete all contents first
                    await foreach (var item in directoryClient.GetFilesAndDirectoriesAsync(cancellationToken: cancellationToken))
                    {
                        if (item.IsDirectory)
                        {
                            var subDirResult = await DeleteDirectoryAsync(CombinePath(path, item.Name), true, cancellationToken);
                            if (subDirResult.IsFailure)
                                return subDirResult;
                        }
                        else
                        {
                            var fileResult = await DeleteFileAsync(CombinePath(path, item.Name), cancellationToken);
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
                _logger.LogError(ex, "Failed to delete directory from Azure: {Path}", path);
                return Result<Unit, FileSystemError>.Failure(
                    new FileSystemError($"Failed to delete directory: {ex.Message}", path));
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
        
        private static string CombinePath(string basePath, string fileName)
        {
            return basePath.TrimEnd('/') + "/" + fileName;
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
            string path,
            string searchPattern,
            Models.EnumerationOptions options,
            int currentDepth,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (options.MaxDepth.HasValue && currentDepth > options.MaxDepth.Value)
                yield break;
            
            var azurePath = GetAzurePath(path);
            var directoryClient = GetDirectoryClient(azurePath);
            var items = directoryClient.GetFilesAndDirectoriesAsync(cancellationToken: cancellationToken);
            
            await foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var fullPath = CombinePath(path, item.Name);
                
                if (item.IsDirectory)
                {
                    if (!options.IncludeHidden && item.Name.StartsWith('.'))
                        continue;
                    
                    if (options.ShouldIncludeDirectory != null && !options.ShouldIncludeDirectory(fullPath))
                        continue;
                    
                    var entry = new FileSystemEntry
                    {
                        Path = fullPath,
                        Name = item.Name,
                        IsDirectory = true,
                        Size = null,
                        ModifiedUtc = DateTime.UtcNow, // Azure doesn't provide this in listing
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
                    
                    if (options.ShouldIncludeFile != null && !options.ShouldIncludeFile(fullPath))
                        continue;
                    
                    var entry = new FileSystemEntry
                    {
                        Path = fullPath,
                        Name = item.Name,
                        IsDirectory = false,
                        Size = null, // Size not available in listing, would need separate API call
                        ModifiedUtc = item.Properties?.LastModified?.DateTime ?? DateTime.UtcNow,
                        Attributes = FileAttributes.Normal
                    };
                    
                    yield return Result<FileSystemEntry, FileSystemError>.Success(entry);
                }
            }
        }
    }
}
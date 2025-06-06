using Azure.Storage.Files.Shares;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.AzureFileStorage;
using Ave.Extensions.FileSystem.AzureFileStorage.Configuration;
using Ave.Extensions.FileSystem.Models;
using FluentAssertions;
using IntegrationTests.Extensions.FileSystem.AzureFileStorage.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

namespace IntegrationTests.Extensions.FileSystem.AzureFileStorage
{
    /// <summary>
    /// Integration tests for Azure File Storage IFileSystem implementation.
    /// 
    /// IMPORTANT: These tests will CLEAR THE ENTIRE AZURE FILE SHARE before running.
    /// Each test run completely empties the configured Azure File Share to ensure 
    /// clean test isolation. Use a dedicated test storage account/share.
    /// 
    /// Tests use a semaphore to ensure only one test instance clears the share at a time,
    /// allowing multiple tests to run in parallel while maintaining proper cleanup.
    /// </summary>
    public class AzureFileStorageIntegrationTests : IAsyncLifetime, IDisposable
    {
        private readonly TestConfiguration _testConfig;
        private readonly IFileSystem? _fileSystem;
        private readonly IReadOnlyFileSystem? _readOnlyFileSystem;
        private readonly ShareClient? _shareClient;
        private readonly string _testDirectoryPath;
        private readonly List<string> _createdFiles;
        private readonly List<string> _createdDirectories;
        private static readonly SemaphoreSlim _shareCleanupSemaphore = new(1, 1);

        public AzureFileStorageIntegrationTests()
        {
            _testConfig = TestConfiguration.Load();
            _testDirectoryPath = $"/test/{DateTime.UtcNow:yyyyMMdd-HHmmssfff}";
            _createdFiles = new List<string>();
            _createdDirectories = new List<string>();

            if (!_testConfig.IsConfigured() || _testConfig.SkipIntegrationTests)
            {
                // FileSystem will be null, tests will be skipped
                return;
            }

            // Setup DI container
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

            var options = new AzureFileSystemOptions
            {
                AccountName = _testConfig.AzureFileStorage.AccountName,
                AccountKey = _testConfig.AzureFileStorage.AccountKey,
                ShareName = _testConfig.AzureFileStorage.ShareName,
                BaseDirectory = _testConfig.AzureFileStorage.BaseDirectory,
                CreateShareIfNotExists = _testConfig.AzureFileStorage.CreateShareIfNotExists,
                Timeout = TimeSpan.FromSeconds(_testConfig.AzureFileStorage.TimeoutSeconds)
            };

            services.AddSingleton(Options.Create(options));
            services.AddSingleton<AzureFileSystem>();
            services.AddSingleton<IFileSystem>(provider => provider.GetRequiredService<AzureFileSystem>());
            services.AddSingleton<IReadOnlyFileSystem>(provider => provider.GetRequiredService<AzureFileSystem>());

            var serviceProvider = services.BuildServiceProvider();
            _fileSystem = serviceProvider.GetRequiredService<IFileSystem>();
            _readOnlyFileSystem = serviceProvider.GetRequiredService<IReadOnlyFileSystem>();

            // Initialize ShareClient for direct verification
            if (_testConfig.IsConfigured())
            {
                var connectionString = $"DefaultEndpointsProtocol=https;AccountName={_testConfig.AzureFileStorage.AccountName};AccountKey={_testConfig.AzureFileStorage.AccountKey};EndpointSuffix=core.windows.net";
                _shareClient = new ShareClient(connectionString, _testConfig.AzureFileStorage.ShareName);
            }
        }

        public async Task InitializeAsync()
        {
            if (!_testConfig.IsConfigured() || _testConfig.SkipIntegrationTests || _fileSystem == null)
                return;

            // Use semaphore to ensure only one test clears the share at a time
            await _shareCleanupSemaphore.WaitAsync();
            try
            {
                // Clear the entire Azure File Share before running tests
                await ClearShareContentsAsync();
                
                // Verify the share is actually empty
                await VerifyShareIsEmptyAsync();
            }
            finally
            {
                _shareCleanupSemaphore.Release();
            }

            // Create the unique test directory for this test instance
            var testDirPath = CanonicalPath.Create(_testDirectoryPath);
            if (testDirPath.IsSuccess)
            {
                await _fileSystem.CreateDirectoryAsync(testDirPath.Value);
                _createdDirectories.Add(_testDirectoryPath);
            }
        }

        private void SkipIfNotConfigured()
        {
            if (!_testConfig.IsConfigured())
            {
                Skip.If(true, "Azure File Storage integration tests are not configured. Use user secrets to configure AzureFileStorage:AccountName, AzureFileStorage:AccountKey, and AzureFileStorage:ShareName");
            }
            
            if (_testConfig.SkipIntegrationTests)
            {
                Skip.If(true, "Integration tests are disabled via SKIP_INTEGRATION_TESTS environment variable");
            }

            if (_fileSystem == null)
            {
                Skip.If(true, "FileSystem is not initialized");
            }
        }

        public async Task DisposeAsync()
        {
            if (!_testConfig.IsConfigured() || _testConfig.SkipIntegrationTests || _fileSystem == null)
                return;

            // Clean up created files and directories
            foreach (var file in _createdFiles)
            {
                try
                {
                    var filePath = CanonicalPath.Create(file);
                    if (filePath.IsSuccess)
                    {
                        await _fileSystem.DeleteFileAsync(filePath.Value);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            foreach (var directory in _createdDirectories.OrderByDescending(d => d.Length))
            {
                try
                {
                    var dirPath = CanonicalPath.Create(directory);
                    if (dirPath.IsSuccess)
                    {
                        await _fileSystem.DeleteDirectoryAsync(dirPath.Value, true);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        [Fact(DisplayName = "AFSIT-001: FileExistsAsync returns true for existing file")]
        public async Task AFSIT001()
        {
            // Arrange
            SkipIfNotConfigured();
            var fileName = "test-file.txt";
            var filePath = $"{_testDirectoryPath}/{fileName}";
            var canonicalPath = CanonicalPath.Create(filePath);
            canonicalPath.IsSuccess.Should().BeTrue();
            _createdFiles.Add(filePath);

            var content = "Test content";
            await _fileSystem!.WriteAllTextAsync(canonicalPath.Value, content);

            // Act
            var result = await _fileSystem.FileExistsAsync(canonicalPath.Value);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeTrue();
            
            // Verify directly with Azure Storage API
            var azureFileExists = await AzureFileExistsAsync(filePath);
            azureFileExists.Should().BeTrue("File should exist in Azure File Share");
        }

        [Fact(DisplayName = "AFSIT-002: FileExistsAsync returns false for non-existent file")]
        public async Task AFSIT002()
        {
            // Arrange
            SkipIfNotConfigured();
            var filePath = $"{_testDirectoryPath}/non-existent.txt";
            var canonicalPath = CanonicalPath.Create(filePath);
            canonicalPath.IsSuccess.Should().BeTrue();

            // Act
            var result = await _fileSystem!.FileExistsAsync(canonicalPath.Value);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeFalse();
        }

        [Fact(DisplayName = "AFSIT-003: DirectoryExistsAsync returns true for existing directory")]
        public async Task AFSIT003()
        {
            // Arrange
            SkipIfNotConfigured();
            var dirPath = $"{_testDirectoryPath}/existing-dir";
            var canonicalPath = CanonicalPath.Create(dirPath);
            canonicalPath.IsSuccess.Should().BeTrue();
            _createdDirectories.Add(dirPath);

            await _fileSystem!.CreateDirectoryAsync(canonicalPath.Value);

            // Act
            var result = await _fileSystem.DirectoryExistsAsync(canonicalPath.Value);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeTrue();
            
            // Verify directly with Azure Storage API
            var azureDirectoryExists = await AzureDirectoryExistsAsync(dirPath);
            azureDirectoryExists.Should().BeTrue("Directory should exist in Azure File Share");
        }

        [Fact(DisplayName = "AFSIT-004: WriteAllTextAsync creates file with content")]
        public async Task AFSIT004()
        {
            // Arrange
            SkipIfNotConfigured();
            var filePath = $"{_testDirectoryPath}/write-test.txt";
            var canonicalPath = CanonicalPath.Create(filePath);
            canonicalPath.IsSuccess.Should().BeTrue();
            _createdFiles.Add(filePath);

            var content = "Hello, Azure File Storage!";

            // Act
            var result = await _fileSystem!.WriteAllTextAsync(canonicalPath.Value, content);

            // Assert
            result.IsSuccess.Should().BeTrue();
            
            // Verify content can be read back
            var readResult = await _fileSystem.ReadAllTextAsync(canonicalPath.Value);
            readResult.IsSuccess.Should().BeTrue();
            readResult.Value.Should().Be(content);
            
            // Verify directly with Azure Storage API
            var azureContent = await AzureReadFileAsync(filePath);
            azureContent.Should().Be(content, "File content should match what was written");
        }

        [Fact(DisplayName = "AFSIT-005: ReadAllTextAsync returns correct content")]
        public async Task AFSIT005()
        {
            // Arrange
            SkipIfNotConfigured();
            var filePath = $"{_testDirectoryPath}/read-test.txt";
            var canonicalPath = CanonicalPath.Create(filePath);
            canonicalPath.IsSuccess.Should().BeTrue();
            _createdFiles.Add(filePath);

            var expectedContent = "Test content for reading";
            await _fileSystem!.WriteAllTextAsync(canonicalPath.Value, expectedContent);

            // Act
            var result = await _fileSystem.ReadAllTextAsync(canonicalPath.Value);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(expectedContent);
        }

        [Fact(DisplayName = "AFSIT-006: ReadAllTextAsync returns error for non-existent file")]
        public async Task AFSIT006()
        {
            // Arrange
            SkipIfNotConfigured();
            var filePath = $"{_testDirectoryPath}/does-not-exist.txt";
            var canonicalPath = CanonicalPath.Create(filePath);
            canonicalPath.IsSuccess.Should().BeTrue();

            // Act
            var result = await _fileSystem!.ReadAllTextAsync(canonicalPath.Value);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Type.Should().Be(FileSystemErrorType.FileNotFound);
        }

        [Fact(DisplayName = "AFSIT-007: CreateDirectoryAsync creates nested directory structure")]
        public async Task AFSIT007()
        {
            // Arrange
            SkipIfNotConfigured();
            var dirPath = $"{_testDirectoryPath}/nested/deep/directory";
            var canonicalPath = CanonicalPath.Create(dirPath);
            canonicalPath.IsSuccess.Should().BeTrue();
            _createdDirectories.Add(dirPath);
            _createdDirectories.Add($"{_testDirectoryPath}/nested/deep");
            _createdDirectories.Add($"{_testDirectoryPath}/nested");

            // Act
            var result = await _fileSystem!.CreateDirectoryAsync(canonicalPath.Value);

            // Assert
            result.IsSuccess.Should().BeTrue();
            
            // Verify directory exists
            var existsResult = await _fileSystem.DirectoryExistsAsync(canonicalPath.Value);
            existsResult.IsSuccess.Should().BeTrue();
            existsResult.Value.Should().BeTrue();
            
            // Verify directly with Azure Storage API
            var azureDirectoryExists = await AzureDirectoryExistsAsync(dirPath);
            azureDirectoryExists.Should().BeTrue("Directory should exist in Azure File Share");
        }

        [Fact(DisplayName = "AFSIT-008: GetFilesAsync returns files in directory")]
        public async Task AFSIT008()
        {
            // Arrange
            SkipIfNotConfigured();
            var subDir = $"{_testDirectoryPath}/list-test";
            var subDirCanonical = CanonicalPath.Create(subDir);
            subDirCanonical.IsSuccess.Should().BeTrue();
            _createdDirectories.Add(subDir);

            var file1 = $"{subDir}/file1.txt";
            var file2 = $"{subDir}/file2.txt";
            var file1Canonical = CanonicalPath.Create(file1);
            var file2Canonical = CanonicalPath.Create(file2);
            file1Canonical.IsSuccess.Should().BeTrue();
            file2Canonical.IsSuccess.Should().BeTrue();
            _createdFiles.Add(file1);
            _createdFiles.Add(file2);

            await _fileSystem!.CreateDirectoryAsync(subDirCanonical.Value);
            await _fileSystem.WriteAllTextAsync(file1Canonical.Value, "Content 1");
            await _fileSystem.WriteAllTextAsync(file2Canonical.Value, "Content 2");

            // Act
            var result = await _fileSystem.GetFilesAsync(subDirCanonical.Value);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().HaveCountGreaterOrEqualTo(2);
            result.Value.Should().Contain(path => path.Value.EndsWith("file1.txt"));
            result.Value.Should().Contain(path => path.Value.EndsWith("file2.txt"));
        }

        [Fact(DisplayName = "AFSIT-009: GetDirectoriesAsync returns directories")]
        public async Task AFSIT009()
        {
            // Arrange
            SkipIfNotConfigured();
            var subDir = $"{_testDirectoryPath}/dir-test";
            var subDirCanonical = CanonicalPath.Create(subDir);
            subDirCanonical.IsSuccess.Should().BeTrue();
            _createdDirectories.Add(subDir);

            var nestedDir1 = $"{subDir}/nested1";
            var nestedDir2 = $"{subDir}/nested2";
            var nested1Canonical = CanonicalPath.Create(nestedDir1);
            var nested2Canonical = CanonicalPath.Create(nestedDir2);
            nested1Canonical.IsSuccess.Should().BeTrue();
            nested2Canonical.IsSuccess.Should().BeTrue();
            _createdDirectories.Add(nestedDir1);
            _createdDirectories.Add(nestedDir2);

            await _fileSystem!.CreateDirectoryAsync(subDirCanonical.Value);
            await _fileSystem.CreateDirectoryAsync(nested1Canonical.Value);
            await _fileSystem.CreateDirectoryAsync(nested2Canonical.Value);

            // Act
            var result = await _fileSystem.GetDirectoriesAsync(subDirCanonical.Value);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().HaveCountGreaterOrEqualTo(2);
            result.Value.Should().Contain(path => path.Value.EndsWith("nested1"));
            result.Value.Should().Contain(path => path.Value.EndsWith("nested2"));
        }

        [Fact(DisplayName = "AFSIT-010: OpenReadAsync and OpenWriteAsync work with streams")]
        public async Task AFSIT010()
        {
            // Arrange
            SkipIfNotConfigured();
            var filePath = $"{_testDirectoryPath}/stream-test.txt";
            var canonicalPath = CanonicalPath.Create(filePath);
            canonicalPath.IsSuccess.Should().BeTrue();
            _createdFiles.Add(filePath);

            var content = "Stream test content";

            // Act - Write using stream
            var writeStreamResult = await _fileSystem!.OpenWriteAsync(canonicalPath.Value);
            writeStreamResult.IsSuccess.Should().BeTrue();
            
            using (var writeStream = writeStreamResult.Value)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(content);
                await writeStream.WriteAsync(bytes, 0, bytes.Length);
            }

            // Act - Read using stream
            var readStreamResult = await _fileSystem.OpenReadAsync(canonicalPath.Value);
            readStreamResult.IsSuccess.Should().BeTrue();
            
            using var readStream = readStreamResult.Value;
            using var reader = new StreamReader(readStream);
            var readContent = await reader.ReadToEndAsync();

            // Assert
            readContent.Should().Be(content);
        }

        [Fact(DisplayName = "AFSIT-011: GetEntryAsync returns correct metadata")]
        public async Task AFSIT011()
        {
            // Arrange
            SkipIfNotConfigured();
            var filePath = $"{_testDirectoryPath}/metadata-test.txt";
            var canonicalPath = CanonicalPath.Create(filePath);
            canonicalPath.IsSuccess.Should().BeTrue();
            _createdFiles.Add(filePath);

            var content = "Test content for metadata";
            await _fileSystem!.WriteAllTextAsync(canonicalPath.Value, content);

            // Act
            var result = await _fileSystem.GetEntryAsync(canonicalPath.Value);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var entry = result.Value;
            
            entry.Path.Should().Be(canonicalPath.Value);
            entry.IsDirectory.Should().BeFalse();
            entry.Size.Should().BeGreaterThan(0);
            entry.Extension.Should().Be(".txt");
            entry.CreatedUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
            entry.ModifiedUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
        }


        [Fact(DisplayName = "AFSIT-013: DeleteFileAsync removes file")]
        public async Task AFSIT013()
        {
            // Arrange
            SkipIfNotConfigured();
            var filePath = $"{_testDirectoryPath}/delete-test.txt";
            var canonicalPath = CanonicalPath.Create(filePath);
            canonicalPath.IsSuccess.Should().BeTrue();

            await _fileSystem!.WriteAllTextAsync(canonicalPath.Value, "To be deleted");
            
            // Verify file exists first
            var existsResult = await _fileSystem.FileExistsAsync(canonicalPath.Value);
            existsResult.IsSuccess.Should().BeTrue();
            existsResult.Value.Should().BeTrue();

            // Act
            var deleteResult = await _fileSystem.DeleteFileAsync(canonicalPath.Value);

            // Assert
            deleteResult.IsSuccess.Should().BeTrue();
            
            // Verify file no longer exists
            var stillExistsResult = await _fileSystem.FileExistsAsync(canonicalPath.Value);
            stillExistsResult.IsSuccess.Should().BeTrue();
            stillExistsResult.Value.Should().BeFalse();
        }

        [Fact(DisplayName = "AFSIT-014: DeleteDirectoryAsync removes directory")]
        public async Task AFSIT014()
        {
            // Arrange
            SkipIfNotConfigured();
            var dirPath = $"{_testDirectoryPath}/delete-dir-test";
            var canonicalPath = CanonicalPath.Create(dirPath);
            canonicalPath.IsSuccess.Should().BeTrue();

            await _fileSystem!.CreateDirectoryAsync(canonicalPath.Value);
            
            // Verify directory exists first
            var existsResult = await _fileSystem.DirectoryExistsAsync(canonicalPath.Value);
            existsResult.IsSuccess.Should().BeTrue();
            existsResult.Value.Should().BeTrue();

            // Act
            var deleteResult = await _fileSystem.DeleteDirectoryAsync(canonicalPath.Value);

            // Assert
            deleteResult.IsSuccess.Should().BeTrue();
            
            // Verify directory no longer exists
            var stillExistsResult = await _fileSystem.DirectoryExistsAsync(canonicalPath.Value);
            stillExistsResult.IsSuccess.Should().BeTrue();
            stillExistsResult.Value.Should().BeFalse();
        }

        [Fact(DisplayName = "AFSIT-015: Large file operations work correctly")]
        public async Task AFSIT015()
        {
            // Arrange
            SkipIfNotConfigured();
            var filePath = $"{_testDirectoryPath}/large-file.txt";
            var canonicalPath = CanonicalPath.Create(filePath);
            canonicalPath.IsSuccess.Should().BeTrue();
            _createdFiles.Add(filePath);

            var largeContent = string.Join("\n", Enumerable.Range(1, 1000).Select(i => $"Line {i}: {new string('x', 100)}"));

            // Act
            var writeResult = await _fileSystem!.WriteAllTextAsync(canonicalPath.Value, largeContent);
            var readResult = await _fileSystem.ReadAllTextAsync(canonicalPath.Value);

            // Assert
            writeResult.IsSuccess.Should().BeTrue();
            readResult.IsSuccess.Should().BeTrue();
            readResult.Value.Should().Be(largeContent);
            readResult.Value.Length.Should().BeGreaterThan(100000);
        }

        [Fact(DisplayName = "AFSIT-016: Unicode content is handled correctly")]
        public async Task AFSIT016()
        {
            // Arrange
            SkipIfNotConfigured();
            var filePath = $"{_testDirectoryPath}/unicode-file.txt";
            var canonicalPath = CanonicalPath.Create(filePath);
            canonicalPath.IsSuccess.Should().BeTrue();
            _createdFiles.Add(filePath);

            var unicodeContent = "Hello 世界! 🌍 Café naïve résumé";

            // Act
            var writeResult = await _fileSystem!.WriteAllTextAsync(canonicalPath.Value, unicodeContent);
            var readResult = await _fileSystem.ReadAllTextAsync(canonicalPath.Value);

            // Assert
            writeResult.IsSuccess.Should().BeTrue();
            readResult.IsSuccess.Should().BeTrue();
            readResult.Value.Should().Be(unicodeContent);
        }

        [Fact(DisplayName = "AFSIT-017: Empty file operations work correctly")]
        public async Task AFSIT017()
        {
            // Arrange
            SkipIfNotConfigured();
            var filePath = $"{_testDirectoryPath}/empty-file.txt";
            var canonicalPath = CanonicalPath.Create(filePath);
            canonicalPath.IsSuccess.Should().BeTrue();
            _createdFiles.Add(filePath);

            var emptyContent = "";

            // Act
            var writeResult = await _fileSystem!.WriteAllTextAsync(canonicalPath.Value, emptyContent);
            var readResult = await _fileSystem.ReadAllTextAsync(canonicalPath.Value);

            // Assert
            writeResult.IsSuccess.Should().BeTrue();
            readResult.IsSuccess.Should().BeTrue();
            readResult.Value.Should().Be(emptyContent);
            
            // Verify directly with Azure Storage API
            var azureContent = await AzureReadFileAsync(filePath);
            azureContent.Should().Be(emptyContent, "Empty file content should be empty");
        }

        // Helper methods for Azure direct verification
        private async Task<bool> AzureFileExistsAsync(string filePath)
        {
            if (_shareClient == null) return false;
            
            try
            {
                var azurePath = ConvertToAzurePath(filePath);
                var fileClient = GetAzureFileClient(azurePath);
                var response = await fileClient.ExistsAsync();
                return response.Value;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> AzureDirectoryExistsAsync(string directoryPath)
        {
            if (_shareClient == null) return false;
            
            try
            {
                var azurePath = ConvertToAzurePath(directoryPath);
                var directoryClient = GetAzureDirectoryClient(azurePath);
                var response = await directoryClient.ExistsAsync();
                return response.Value;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string?> AzureReadFileAsync(string filePath)
        {
            if (_shareClient == null) return null;
            
            try
            {
                var azurePath = ConvertToAzurePath(filePath);
                var fileClient = GetAzureFileClient(azurePath);
                var response = await fileClient.DownloadAsync();
                using var reader = new StreamReader(response.Value.Content);
                return await reader.ReadToEndAsync();
            }
            catch
            {
                return null;
            }
        }

        private ShareFileClient GetAzureFileClient(string azurePath)
        {
            var segments = azurePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            if (segments.Length == 0)
                throw new ArgumentException("Invalid file path");
            
            ShareDirectoryClient dirClient = _shareClient!.GetRootDirectoryClient();
            
            // Navigate through directories
            for (int i = 0; i < segments.Length - 1; i++)
            {
                dirClient = dirClient.GetSubdirectoryClient(segments[i]);
            }
            
            // Get file client for the last segment
            return dirClient.GetFileClient(segments[segments.Length - 1]);
        }
        
        private ShareDirectoryClient GetAzureDirectoryClient(string azurePath)
        {
            if (string.IsNullOrEmpty(azurePath))
                return _shareClient!.GetRootDirectoryClient();
            
            var segments = azurePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            ShareDirectoryClient dirClient = _shareClient!.GetRootDirectoryClient();
            
            foreach (var segment in segments)
            {
                dirClient = dirClient.GetSubdirectoryClient(segment);
            }
            
            return dirClient;
        }

        private string ConvertToAzurePath(string path)
        {
            // Convert from canonical path format to Azure path format
            var normalizedPath = path.Replace('\\', '/').Replace("//", "/").TrimStart('/');
            
            if (!string.IsNullOrEmpty(_testConfig.AzureFileStorage.BaseDirectory))
            {
                var baseDir = _testConfig.AzureFileStorage.BaseDirectory.Replace('\\', '/').Replace("//", "/").TrimStart('/');
                if (!normalizedPath.StartsWith(baseDir + "/", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedPath = $"{baseDir}/{normalizedPath}";
                }
            }
            
            return normalizedPath;
        }

        private async Task ClearShareContentsAsync()
        {
            if (_shareClient == null)
            {
                Console.WriteLine("⚠️  ShareClient not initialized - skipping share cleanup");
                return;
            }

            try
            {
                Console.WriteLine($"🧹 Starting cleanup of Azure File Share: {_testConfig.AzureFileStorage.ShareName}");

                // Check if share exists, if not, create it
                if (!await _shareClient.ExistsAsync())
                {
                    Console.WriteLine("📁 Creating new Azure File Share as it doesn't exist");
                    await _shareClient.CreateAsync();
                    Console.WriteLine("✅ Azure File Share created and is empty");
                    return; // If share was just created, it's already empty
                }

                // Get the root directory client
                var rootDirectory = _shareClient.GetRootDirectoryClient();

                Console.WriteLine("🗑️  Clearing all existing contents from Azure File Share...");
                // Clear all contents in the share
                await ClearDirectoryContentsAsync(rootDirectory);
                Console.WriteLine("✅ Azure File Share cleanup completed");
            }
            catch (Exception ex)
            {
                // Log but don't fail the test setup - the tests themselves will fail if there are issues
                Console.WriteLine($"❌ Warning: Failed to clear share contents: {ex.Message}");
            }
        }

        private async Task ClearDirectoryContentsAsync(ShareDirectoryClient directoryClient)
        {
            try
            {
                var itemsToDelete = new List<string>();
                
                // First, collect all items in this directory
                await foreach (var item in directoryClient.GetFilesAndDirectoriesAsync())
                {
                    itemsToDelete.Add($"{(item.IsDirectory ? "📁" : "📄")} {item.Name}");
                    
                    if (item.IsDirectory)
                    {
                        // Recursively clear subdirectory contents first
                        var subDirectoryClient = directoryClient.GetSubdirectoryClient(item.Name);
                        await ClearDirectoryContentsAsync(subDirectoryClient);
                        
                        // Then delete the empty subdirectory
                        await subDirectoryClient.DeleteAsync();
                        Console.WriteLine($"🗑️  Deleted directory: {item.Name}");
                    }
                    else
                    {
                        // Delete the file
                        var fileClient = directoryClient.GetFileClient(item.Name);
                        await fileClient.DeleteAsync();
                        Console.WriteLine($"🗑️  Deleted file: {item.Name}");
                    }
                }

                if (itemsToDelete.Count > 0)
                {
                    Console.WriteLine($"📋 Cleaned {itemsToDelete.Count} items from directory");
                }
            }
            catch (Exception ex)
            {
                // Log but continue - some files might be locked or have permission issues
                Console.WriteLine($"❌ Warning: Failed to clear directory contents: {ex.Message}");
            }
        }

        private async Task VerifyShareIsEmptyAsync()
        {
            if (_shareClient == null) return;

            try
            {
                var rootDirectory = _shareClient.GetRootDirectoryClient();
                var items = new List<string>();
                
                await foreach (var item in rootDirectory.GetFilesAndDirectoriesAsync())
                {
                    items.Add($"{(item.IsDirectory ? "Directory" : "File")}: {item.Name}");
                }

                if (items.Count > 0)
                {
                    var itemsList = string.Join(", ", items);
                    Console.WriteLine($"Warning: Azure File Share is not empty after cleanup. Remaining items: {itemsList}");
                }
                else
                {
                    Console.WriteLine("✅ Azure File Share verified as empty before test execution");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to verify share is empty: {ex.Message}");
            }
        }

        public void Dispose()
        {
            // Semaphore is static and shared across all test instances
            // It will be disposed when the application domain shuts down
            GC.SuppressFinalize(this);
        }
    }
}
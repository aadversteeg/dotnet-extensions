using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Local.PathMappers;
using Ave.Extensions.FileSystem.Models;
using FluentAssertions;
using IntegrationTests.FileSystem.Local.TestHelpers;
using Xunit;

namespace IntegrationTests.FileSystem.Local
{
    /// <summary>
    /// Cross-platform integration tests for LocalFileSystem class.
    /// These tests work on both Windows and Linux/WSL by using System.IO for setup/assertions
    /// and testing the LocalFileSystem through its canonical path interface.
    /// </summary>
    public class LocalFileSystemCrossplatformIntegrationTests : IAsyncLifetime, IDisposable
    {
        private readonly LocalFileSystem _fileSystem;
        private readonly IPathMapper? _pathMapper;
        private readonly string _physicalTestDirectory;
        private readonly CanonicalPath _canonicalTestDirectory;
        private readonly List<string> _createdFiles;
        private readonly List<string> _createdDirectories;

        public LocalFileSystemCrossplatformIntegrationTests()
        {
            _createdFiles = new List<string>();
            _createdDirectories = new List<string>();

            // Create platform-appropriate path mapper
            _pathMapper = CreatePlatformSpecificPathMapper();
            
            // Create test directory using System.IO
            var testInstanceId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
            _physicalTestDirectory = Path.Combine(Path.GetTempPath(), $"LocalFileSystemIT_{testInstanceId}");
            
            // Convert to canonical path
            _canonicalTestDirectory = ConvertToCanonicalPath(_physicalTestDirectory);
            
            // Create filesystem instance for testing
            _fileSystem = new LocalFileSystem(_pathMapper);
        }

        public Task InitializeAsync()
        {
            // Use System.IO to create the test directory
            Directory.CreateDirectory(_physicalTestDirectory);
            _createdDirectories.Add(_physicalTestDirectory);
            
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            try
            {
                // Clean up using System.IO
                if (Directory.Exists(_physicalTestDirectory))
                {
                    Directory.Delete(_physicalTestDirectory, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        #region Basic File Operations

        [Fact(DisplayName = "LFSIT-001: FileExistsAsync returns true for existing file")]
        public async Task LFSIT001()
        {
            // Arrange - create file using System.IO
            var fileName = "test-file.txt";
            var physicalFilePath = Path.Combine(_physicalTestDirectory, fileName);
            var canonicalFilePath = GetCanonicalFilePath(fileName);
            
            File.WriteAllText(physicalFilePath, "test content");
            _createdFiles.Add(physicalFilePath);

            // Act - test through LocalFileSystem
            var result = await _fileSystem.FileExistsAsync(canonicalFilePath);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeTrue();
            
            // Verify with System.IO
            File.Exists(physicalFilePath).Should().BeTrue();
        }

        [Fact(DisplayName = "LFSIT-002: FileExistsAsync returns false for non-existing file")]
        public async Task LFSIT002()
        {
            // Arrange
            var fileName = "non-existing-file.txt";
            var physicalFilePath = Path.Combine(_physicalTestDirectory, fileName);
            var canonicalFilePath = GetCanonicalFilePath(fileName);

            // Verify file doesn't exist with System.IO
            File.Exists(physicalFilePath).Should().BeFalse();

            // Act
            var result = await _fileSystem.FileExistsAsync(canonicalFilePath);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeFalse();
        }

        [Fact(DisplayName = "LFSIT-003: DirectoryExistsAsync returns true for existing directory")]
        public async Task LFSIT003()
        {
            // Arrange - create directory using System.IO
            var dirName = "test-directory";
            var physicalDirPath = Path.Combine(_physicalTestDirectory, dirName);
            var canonicalDirPath = GetCanonicalDirectoryPath(dirName);
            
            Directory.CreateDirectory(physicalDirPath);
            _createdDirectories.Add(physicalDirPath);

            // Act
            var result = await _fileSystem.DirectoryExistsAsync(canonicalDirPath);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeTrue();
            
            // Verify with System.IO
            Directory.Exists(physicalDirPath).Should().BeTrue();
        }

        [Fact(DisplayName = "LFSIT-004: DirectoryExistsAsync returns false for non-existing directory")]
        public async Task LFSIT004()
        {
            // Arrange
            var dirName = "non-existing-directory";
            var physicalDirPath = Path.Combine(_physicalTestDirectory, dirName);
            var canonicalDirPath = GetCanonicalDirectoryPath(dirName);

            // Verify directory doesn't exist with System.IO
            Directory.Exists(physicalDirPath).Should().BeFalse();

            // Act
            var result = await _fileSystem.DirectoryExistsAsync(canonicalDirPath);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeFalse();
        }

        [Fact(DisplayName = "LFSIT-005: WriteAllTextAsync creates file with correct content")]
        public async Task LFSIT005()
        {
            // Arrange
            var fileName = "write-test.txt";
            var physicalFilePath = Path.Combine(_physicalTestDirectory, fileName);
            var canonicalFilePath = GetCanonicalFilePath(fileName);
            var content = "Hello, cross-platform world!";
            _createdFiles.Add(physicalFilePath);

            // Act
            var result = await _fileSystem.WriteAllTextAsync(canonicalFilePath, content);

            // Assert
            result.IsSuccess.Should().BeTrue();
            
            // Verify with System.IO
            File.Exists(physicalFilePath).Should().BeTrue();
            var actualContent = File.ReadAllText(physicalFilePath);
            actualContent.Should().Be(content);
        }

        [Fact(DisplayName = "LFSIT-006: ReadAllTextAsync retrieves correct content")]
        public async Task LFSIT006()
        {
            // Arrange - create file using System.IO
            var fileName = "read-test.txt";
            var physicalFilePath = Path.Combine(_physicalTestDirectory, fileName);
            var canonicalFilePath = GetCanonicalFilePath(fileName);
            var expectedContent = "Content to read back";
            
            File.WriteAllText(physicalFilePath, expectedContent);
            _createdFiles.Add(physicalFilePath);

            // Act
            var result = await _fileSystem.ReadAllTextAsync(canonicalFilePath);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(expectedContent);
        }

        [Fact(DisplayName = "LFSIT-007: ReadAllTextAsync returns error for non-existing file")]
        public async Task LFSIT007()
        {
            // Arrange
            var fileName = "non-existing.txt";
            var physicalFilePath = Path.Combine(_physicalTestDirectory, fileName);
            var canonicalFilePath = GetCanonicalFilePath(fileName);

            // Verify file doesn't exist with System.IO
            File.Exists(physicalFilePath).Should().BeFalse();

            // Act
            var result = await _fileSystem.ReadAllTextAsync(canonicalFilePath);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Type.Should().Be(FileSystemErrorType.FileNotFound);
        }

        #endregion

        #region Stream Operations

        [Fact(DisplayName = "LFSIT-008: OpenReadAsync returns valid stream")]
        public async Task LFSIT008()
        {
            // Arrange - create file using System.IO
            var fileName = "stream-read-test.txt";
            var physicalFilePath = Path.Combine(_physicalTestDirectory, fileName);
            var canonicalFilePath = GetCanonicalFilePath(fileName);
            var content = "Stream content test";
            
            File.WriteAllText(physicalFilePath, content);
            _createdFiles.Add(physicalFilePath);

            // Act
            var result = await _fileSystem.OpenReadAsync(canonicalFilePath);

            // Assert
            result.IsSuccess.Should().BeTrue();
            
            using var stream = result.Value;
            using var reader = new StreamReader(stream);
            var readContent = await reader.ReadToEndAsync();
            readContent.Should().Be(content);
        }

        [Fact(DisplayName = "LFSIT-009: OpenWriteAsync creates writable stream")]
        public async Task LFSIT009()
        {
            // Arrange
            var fileName = "stream-write-test.txt";
            var physicalFilePath = Path.Combine(_physicalTestDirectory, fileName);
            var canonicalFilePath = GetCanonicalFilePath(fileName);
            var content = "Written via stream";
            _createdFiles.Add(physicalFilePath);

            // Act
            var result = await _fileSystem.OpenWriteAsync(canonicalFilePath);
            result.IsSuccess.Should().BeTrue();

            using (var stream = result.Value)
            using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(content);
            }

            // Assert with System.IO
            File.Exists(physicalFilePath).Should().BeTrue();
            var actualContent = File.ReadAllText(physicalFilePath);
            actualContent.Should().Be(content);
        }

        #endregion

        #region Directory Operations

        [Fact(DisplayName = "LFSIT-010: CreateDirectoryAsync creates directory structure")]
        public async Task LFSIT010()
        {
            // Arrange
            var nestedPath = "nested/deep/structure"; // Use forward slashes for canonical paths
            var physicalDirPath = Path.Combine(_physicalTestDirectory, nestedPath.Replace('/', Path.DirectorySeparatorChar));
            var canonicalDirPath = GetCanonicalDirectoryPath(nestedPath);
            _createdDirectories.Add(physicalDirPath);

            // Verify directory doesn't exist with System.IO
            Directory.Exists(physicalDirPath).Should().BeFalse();

            // Act
            var result = await _fileSystem.CreateDirectoryAsync(canonicalDirPath);

            // Assert
            result.IsSuccess.Should().BeTrue();
            
            // Verify with System.IO
            Directory.Exists(physicalDirPath).Should().BeTrue();
        }

        [Fact(DisplayName = "LFSIT-011: GetFilesAsync returns correct file list")]
        public async Task LFSIT011()
        {
            // Arrange - create test files using System.IO
            var subDirName = "files-test";
            var physicalSubDir = Path.Combine(_physicalTestDirectory, subDirName);
            var canonicalSubDir = GetCanonicalDirectoryPath(subDirName);
            
            Directory.CreateDirectory(physicalSubDir);
            _createdDirectories.Add(physicalSubDir);

            var file1Path = Path.Combine(physicalSubDir, "file1.txt");
            var file2Path = Path.Combine(physicalSubDir, "file2.txt");
            var file3Path = Path.Combine(physicalSubDir, "file3.log");
            
            File.WriteAllText(file1Path, "Content 1");
            File.WriteAllText(file2Path, "Content 2");
            File.WriteAllText(file3Path, "Log content");
            
            _createdFiles.AddRange(new[] { file1Path, file2Path, file3Path });

            // Act - get all files
            var allFilesResult = await _fileSystem.GetFilesAsync(canonicalSubDir);
            
            // Act - get only .txt files
            var txtFilesResult = await _fileSystem.GetFilesAsync(canonicalSubDir, "*.txt");

            // Assert
            allFilesResult.IsSuccess.Should().BeTrue();
            allFilesResult.Value.Should().HaveCount(3);
            
            txtFilesResult.IsSuccess.Should().BeTrue();
            txtFilesResult.Value.Should().HaveCount(2);
            
            // Verify files exist with System.IO
            File.Exists(file1Path).Should().BeTrue();
            File.Exists(file2Path).Should().BeTrue();
            File.Exists(file3Path).Should().BeTrue();
        }

        [Fact(DisplayName = "LFSIT-012: GetDirectoriesAsync returns correct directory list")]
        public async Task LFSIT012()
        {
            // Arrange - create test directories using System.IO
            var baseDirName = "directories-test";
            var physicalBaseDir = Path.Combine(_physicalTestDirectory, baseDirName);
            var canonicalBaseDir = GetCanonicalDirectoryPath(baseDirName);
            
            Directory.CreateDirectory(physicalBaseDir);
            _createdDirectories.Add(physicalBaseDir);

            var subDir1 = Path.Combine(physicalBaseDir, "subdir1");
            var subDir2 = Path.Combine(physicalBaseDir, "subdir2");
            var subDir3 = Path.Combine(physicalBaseDir, "other");
            
            Directory.CreateDirectory(subDir1);
            Directory.CreateDirectory(subDir2);
            Directory.CreateDirectory(subDir3);
            
            _createdDirectories.AddRange(new[] { subDir1, subDir2, subDir3 });

            // Act - get all directories
            var allDirsResult = await _fileSystem.GetDirectoriesAsync(canonicalBaseDir);
            
            // Act - get directories matching pattern
            var patternDirsResult = await _fileSystem.GetDirectoriesAsync(canonicalBaseDir, "subdir*");

            // Assert
            allDirsResult.IsSuccess.Should().BeTrue();
            allDirsResult.Value.Should().HaveCount(3);
            
            patternDirsResult.IsSuccess.Should().BeTrue();
            patternDirsResult.Value.Should().HaveCount(2);
            
            // Verify directories exist with System.IO
            Directory.Exists(subDir1).Should().BeTrue();
            Directory.Exists(subDir2).Should().BeTrue();
            Directory.Exists(subDir3).Should().BeTrue();
        }

        [Fact(DisplayName = "LFSIT-013: DeleteFileAsync removes file successfully")]
        public async Task LFSIT013()
        {
            // Arrange - create file using System.IO
            var fileName = "delete-file-test.txt";
            var physicalFilePath = Path.Combine(_physicalTestDirectory, fileName);
            var canonicalFilePath = GetCanonicalFilePath(fileName);
            
            File.WriteAllText(physicalFilePath, "File to delete");
            
            // Verify file exists with System.IO
            File.Exists(physicalFilePath).Should().BeTrue();

            // Act
            var result = await _fileSystem.DeleteFileAsync(canonicalFilePath);

            // Assert
            result.IsSuccess.Should().BeTrue();
            
            // Verify with System.IO
            File.Exists(physicalFilePath).Should().BeFalse();
        }

        [Fact(DisplayName = "LFSIT-014: DeleteDirectoryAsync removes directory successfully")]
        public async Task LFSIT014()
        {
            // Arrange - create directory using System.IO
            var dirName = "delete-dir-test";
            var physicalDirPath = Path.Combine(_physicalTestDirectory, dirName);
            var canonicalDirPath = GetCanonicalDirectoryPath(dirName);
            
            Directory.CreateDirectory(physicalDirPath);
            
            // Verify directory exists with System.IO
            Directory.Exists(physicalDirPath).Should().BeTrue();

            // Act
            var result = await _fileSystem.DeleteDirectoryAsync(canonicalDirPath);

            // Assert
            result.IsSuccess.Should().BeTrue();
            
            // Verify with System.IO
            Directory.Exists(physicalDirPath).Should().BeFalse();
        }

        [Fact(DisplayName = "LFSIT-015: DeleteDirectoryAsync with recursive removes nested structure")]
        public async Task LFSIT015()
        {
            // Arrange - create nested structure using System.IO
            var baseDirName = "recursive-delete-test";
            var physicalBaseDir = Path.Combine(_physicalTestDirectory, baseDirName);
            var canonicalBaseDir = GetCanonicalDirectoryPath(baseDirName);
            
            var nestedDir = Path.Combine(physicalBaseDir, "nested");
            var nestedFile = Path.Combine(nestedDir, "file.txt");
            
            Directory.CreateDirectory(nestedDir);
            File.WriteAllText(nestedFile, "Nested file content");
            
            // Verify structure exists with System.IO
            Directory.Exists(physicalBaseDir).Should().BeTrue();
            Directory.Exists(nestedDir).Should().BeTrue();
            File.Exists(nestedFile).Should().BeTrue();

            // Act
            var result = await _fileSystem.DeleteDirectoryAsync(canonicalBaseDir, recursive: true);

            // Assert
            result.IsSuccess.Should().BeTrue();
            
            // Verify with System.IO
            Directory.Exists(physicalBaseDir).Should().BeFalse();
            Directory.Exists(nestedDir).Should().BeFalse();
            File.Exists(nestedFile).Should().BeFalse();
        }

        #endregion

        #region Metadata Operations

        [Fact(DisplayName = "LFSIT-016: GetEntryAsync returns correct file metadata")]
        public async Task LFSIT016()
        {
            // Arrange - create file using System.IO
            var fileName = "metadata-file-test.txt";
            var physicalFilePath = Path.Combine(_physicalTestDirectory, fileName);
            var canonicalFilePath = GetCanonicalFilePath(fileName);
            var content = "Metadata test content";
            
            File.WriteAllText(physicalFilePath, content);
            _createdFiles.Add(physicalFilePath);

            // Get expected metadata using System.IO
            var fileInfo = new FileInfo(physicalFilePath);

            // Act
            var result = await _fileSystem.GetEntryAsync(canonicalFilePath);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var entry = result.Value;
            
            entry.Name.Should().Be(fileName);
            entry.Size.Should().Be(Encoding.UTF8.GetBytes(content).Length);
            entry.IsDirectory.Should().BeFalse();
            entry.Extension.Should().Be(".txt");
            entry.Attributes.Should().Be(fileInfo.Attributes);
        }

        [Fact(DisplayName = "LFSIT-017: GetEntryAsync returns correct directory metadata")]
        public async Task LFSIT017()
        {
            // Arrange - create directory using System.IO
            var dirName = "metadata-dir-test";
            var physicalDirPath = Path.Combine(_physicalTestDirectory, dirName);
            var canonicalDirPath = GetCanonicalDirectoryPath(dirName);
            
            Directory.CreateDirectory(physicalDirPath);
            _createdDirectories.Add(physicalDirPath);

            // Get expected metadata using System.IO
            var dirInfo = new DirectoryInfo(physicalDirPath);

            // Act
            var result = await _fileSystem.GetEntryAsync(canonicalDirPath);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var entry = result.Value;
            
            entry.Name.Should().Be(dirName);
            entry.Size.Should().BeNull();
            entry.IsDirectory.Should().BeTrue();
            entry.Extension.Should().BeNull();
            entry.Attributes.Should().Be(dirInfo.Attributes);
        }

        [Fact(DisplayName = "LFSIT-018: GetEntryAsync returns error for non-existing path")]
        public async Task LFSIT018()
        {
            // Arrange
            var fileName = "non-existing-metadata.txt";
            var physicalFilePath = Path.Combine(_physicalTestDirectory, fileName);
            var canonicalFilePath = GetCanonicalFilePath(fileName);

            // Verify file doesn't exist with System.IO
            File.Exists(physicalFilePath).Should().BeFalse();

            // Act
            var result = await _fileSystem.GetEntryAsync(canonicalFilePath);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Type.Should().Be(FileSystemErrorType.PathNotFound);
        }

        #endregion

        #region Enumeration Operations

        [Fact(DisplayName = "LFSIT-019: EnumerateFileSystemEntriesAsync returns all entries")]
        public async Task LFSIT019()
        {
            // Arrange - create mixed structure using System.IO
            var baseDirName = "enumeration-test";
            var physicalBaseDir = Path.Combine(_physicalTestDirectory, baseDirName);
            var canonicalBaseDir = GetCanonicalDirectoryPath(baseDirName);
            
            Directory.CreateDirectory(physicalBaseDir);
            _createdDirectories.Add(physicalBaseDir);

            var subDir = Path.Combine(physicalBaseDir, "subdir");
            var file1 = Path.Combine(physicalBaseDir, "file1.txt");
            var file2 = Path.Combine(physicalBaseDir, "file2.txt");
            
            Directory.CreateDirectory(subDir);
            File.WriteAllText(file1, "Content 1");
            File.WriteAllText(file2, "Content 2");
            
            _createdDirectories.Add(subDir);
            _createdFiles.AddRange(new[] { file1, file2 });

            // Act
            var entries = new List<FileSystemEntry>();
            await foreach (var result in _fileSystem.EnumerateFileSystemEntriesAsync(canonicalBaseDir))
            {
                result.IsSuccess.Should().BeTrue();
                entries.Add(result.Value);
            }

            // Assert
            entries.Should().HaveCount(3); // 1 directory + 2 files
            entries.Should().Contain(e => e.Name == "subdir" && e.IsDirectory);
            entries.Should().Contain(e => e.Name == "file1.txt" && !e.IsDirectory);
            entries.Should().Contain(e => e.Name == "file2.txt" && !e.IsDirectory);
        }

        #endregion

        #region Cross-Platform Compatibility Tests

        [Fact(DisplayName = "LFSIT-020: Handles Unicode content correctly")]
        public async Task LFSIT020()
        {
            // Arrange
            var fileName = "unicode-test.txt";
            var physicalFilePath = Path.Combine(_physicalTestDirectory, fileName);
            var canonicalFilePath = GetCanonicalFilePath(fileName);
            var unicodeContent = "Hello 世界! 🌍 Café naïve résumé ñoño";
            _createdFiles.Add(physicalFilePath);

            // Act
            var writeResult = await _fileSystem.WriteAllTextAsync(canonicalFilePath, unicodeContent);
            var readResult = await _fileSystem.ReadAllTextAsync(canonicalFilePath);

            // Assert
            writeResult.IsSuccess.Should().BeTrue();
            readResult.IsSuccess.Should().BeTrue();
            readResult.Value.Should().Be(unicodeContent);
            
            // Verify with System.IO
            var systemContent = File.ReadAllText(physicalFilePath);
            systemContent.Should().Be(unicodeContent);
        }

        [Fact(DisplayName = "LFSIT-021: Handles large files correctly")]
        public async Task LFSIT021()
        {
            // Arrange - create large content
            var fileName = "large-file-test.txt";
            var physicalFilePath = Path.Combine(_physicalTestDirectory, fileName);
            var canonicalFilePath = GetCanonicalFilePath(fileName);
            var largeContent = string.Join("\n", Enumerable.Range(1, 10000).Select(i => $"Line {i}: {new string('x', 100)}"));
            _createdFiles.Add(physicalFilePath);

            // Act
            var writeResult = await _fileSystem.WriteAllTextAsync(canonicalFilePath, largeContent);
            var readResult = await _fileSystem.ReadAllTextAsync(canonicalFilePath);

            // Assert
            writeResult.IsSuccess.Should().BeTrue();
            readResult.IsSuccess.Should().BeTrue();
            readResult.Value.Should().Be(largeContent);
            readResult.Value.Length.Should().BeGreaterThan(1000000); // Verify it's actually large
            
            // Verify with System.IO
            File.Exists(physicalFilePath).Should().BeTrue();
            var fileInfo = new FileInfo(physicalFilePath);
            fileInfo.Length.Should().BeGreaterThan(1000000);
        }

        [Fact(DisplayName = "LFSIT-022: Cancellation token is respected")]
        public async Task LFSIT022()
        {
            // Arrange
            var fileName = "cancellation-test.txt";
            var canonicalFilePath = GetCanonicalFilePath(fileName);
            var content = "Test content";
            
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => 
                await _fileSystem.WriteAllTextAsync(canonicalFilePath, content, cts.Token));
        }

        #endregion

        #region Helper Methods

        private static IPathMapper CreatePlatformSpecificPathMapper()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? new WindowsPathMapper() 
                : new UnixPathMapper();
        }

        private CanonicalPath ConvertToCanonicalPath(string physicalPath)
        {
            if (_pathMapper != null)
            {
                var result = _pathMapper.MapToCanonicalPath(physicalPath);
                if (result.IsSuccess)
                    return result.Value;
            }
            
            // Fallback: normalize to Unix-style path
            var normalizedPath = physicalPath.Replace('\\', '/');
            if (!normalizedPath.StartsWith('/'))
                normalizedPath = "/" + normalizedPath;
            
            return normalizedPath.ToCanonicalPath();
        }

        private CanonicalPath GetCanonicalFilePath(string fileName)
        {
            return (_canonicalTestDirectory.Value + "/" + fileName).ToCanonicalPath();
        }

        private CanonicalPath GetCanonicalDirectoryPath(string directoryName)
        {
            return (_canonicalTestDirectory.Value + "/" + directoryName).ToCanonicalPath();
        }

        #endregion
    }
}
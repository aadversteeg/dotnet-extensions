using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.InMemory;
using Ave.Extensions.FileSystem.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace UnitTests.Extensions.FileSystem.InMemory
{
    /// <summary>
    /// Unit tests for InMemoryFileSystem.
    /// </summary>
    public class InMemoryFileSystemTests : IDisposable
    {
        private readonly InMemoryFileSystem _fileSystem;
        private readonly Mock<ILogger<InMemoryFileSystem>> _mockLogger;

        public InMemoryFileSystemTests()
        {
            _mockLogger = new Mock<ILogger<InMemoryFileSystem>>();
            _fileSystem = new InMemoryFileSystem(_mockLogger.Object);
        }

        [Fact(DisplayName = "IMFS-001: FileExistsAsync returns false for non-existing file")]
        public async Task IMFS001()
        {
            // Arrange
            var path = "/test/file.txt";

            // Act
            var result = await _fileSystem.FileExistsAsync(path);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeFalse();
        }

        [Fact(DisplayName = "IMFS-002: FileExistsAsync returns true for existing file")]
        public async Task IMFS002()
        {
            // Arrange
            var path = "/test/file.txt";
            var content = "Hello, World!";
            await _fileSystem.WriteAllTextAsync(path, content);

            // Act
            var result = await _fileSystem.FileExistsAsync(path);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeTrue();
        }

        [Fact(DisplayName = "IMFS-003: DirectoryExistsAsync returns true for root directory")]
        public async Task IMFS003()
        {
            // Arrange
            var path = "/";

            // Act
            var result = await _fileSystem.DirectoryExistsAsync(path);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeTrue();
        }

        [Fact(DisplayName = "IMFS-004: DirectoryExistsAsync returns false for non-existing directory")]
        public async Task IMFS004()
        {
            // Arrange
            var path = "/test/directory";

            // Act
            var result = await _fileSystem.DirectoryExistsAsync(path);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeFalse();
        }

        [Fact(DisplayName = "IMFS-005: CreateDirectoryAsync creates directory successfully")]
        public async Task IMFS005()
        {
            // Arrange
            var path = "/test/directory";

            // Act
            var result = await _fileSystem.CreateDirectoryAsync(path);

            // Assert
            result.IsSuccess.Should().BeTrue();
            
            var existsResult = await _fileSystem.DirectoryExistsAsync(path);
            existsResult.IsSuccess.Should().BeTrue();
            existsResult.Value.Should().BeTrue();
        }

        [Fact(DisplayName = "IMFS-006: WriteAllTextAsync creates file with content")]
        public async Task IMFS006()
        {
            // Arrange
            var path = "/test/file.txt";
            var content = "Hello, World!";

            // Act
            var result = await _fileSystem.WriteAllTextAsync(path, content);

            // Assert
            result.IsSuccess.Should().BeTrue();
            
            var readResult = await _fileSystem.ReadAllTextAsync(path);
            readResult.IsSuccess.Should().BeTrue();
            readResult.Value.Should().Be(content);
        }

        [Fact(DisplayName = "IMFS-007: ReadAllTextAsync returns failure for non-existing file")]
        public async Task IMFS007()
        {
            // Arrange
            var path = "/test/nonexistent.txt";

            // Act
            var result = await _fileSystem.ReadAllTextAsync(path);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Type.Should().Be(FileSystemErrorType.FileNotFound);
        }

        [Fact(DisplayName = "IMFS-008: OpenReadAsync returns valid stream")]
        public async Task IMFS008()
        {
            // Arrange
            var path = "/test/file.txt";
            var content = "Hello, World!";
            await _fileSystem.WriteAllTextAsync(path, content);

            // Act
            var result = await _fileSystem.OpenReadAsync(path);

            // Assert
            result.IsSuccess.Should().BeTrue();
            using var stream = result.Value;
            using var reader = new StreamReader(stream);
            var readContent = await reader.ReadToEndAsync();
            readContent.Should().Be(content);
        }

        [Fact(DisplayName = "IMFS-009: OpenWriteAsync creates file when disposed")]
        public async Task IMFS009()
        {
            // Arrange
            var path = "/test/file.txt";
            var content = "Hello, World!";

            // Act
            var result = await _fileSystem.OpenWriteAsync(path);
            result.IsSuccess.Should().BeTrue();

            using (var stream = result.Value)
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                await stream.WriteAsync(bytes, 0, bytes.Length);
            }

            // Assert
            var readResult = await _fileSystem.ReadAllTextAsync(path);
            readResult.IsSuccess.Should().BeTrue();
            readResult.Value.Should().Be(content);
        }

        [Fact(DisplayName = "IMFS-010: GetFileMetadataAsync returns correct metadata")]
        public async Task IMFS010()
        {
            // Arrange
            var path = "/test/file.txt";
            var content = "Hello, World!";
            var beforeWrite = DateTime.UtcNow;
            await _fileSystem.WriteAllTextAsync(path, content);
            var afterWrite = DateTime.UtcNow;

            // Act
            var result = await _fileSystem.GetFileMetadataAsync(path);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var metadata = result.Value;
            metadata.FullPath.Should().Be(path);
            metadata.Name.Should().Be("file.txt");
            metadata.Size.Should().Be(Encoding.UTF8.GetBytes(content).Length);
            metadata.CreatedUtc.Should().BeOnOrAfter(beforeWrite).And.BeOnOrBefore(afterWrite);
            metadata.ModifiedUtc.Should().BeOnOrAfter(beforeWrite).And.BeOnOrBefore(afterWrite);
            metadata.Attributes.Should().Be(FileAttributes.Normal);
        }

        [Fact(DisplayName = "IMFS-011: GetDirectoryMetadataAsync returns correct metadata")]
        public async Task IMFS011()
        {
            // Arrange
            var path = "/test/directory";
            var beforeCreate = DateTime.UtcNow;
            await _fileSystem.CreateDirectoryAsync(path);
            var afterCreate = DateTime.UtcNow;

            // Act
            var result = await _fileSystem.GetDirectoryMetadataAsync(path);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var metadata = result.Value;
            metadata.FullPath.Should().Be(path);
            metadata.Name.Should().Be("directory");
            metadata.CreatedUtc.Should().BeOnOrAfter(beforeCreate).And.BeOnOrBefore(afterCreate);
            metadata.ModifiedUtc.Should().BeOnOrAfter(beforeCreate).And.BeOnOrBefore(afterCreate);
            metadata.Attributes.Should().Be(FileAttributes.Directory);
        }

        [Fact(DisplayName = "IMFS-012: GetFilesAsync returns correct file list")]
        public async Task IMFS012()
        {
            // Arrange
            var directory = "/test";
            await _fileSystem.CreateDirectoryAsync(directory);
            await _fileSystem.WriteAllTextAsync("/test/file1.txt", "content1");
            await _fileSystem.WriteAllTextAsync("/test/file2.txt", "content2");
            await _fileSystem.WriteAllTextAsync("/test/file3.log", "content3");

            // Act
            var result = await _fileSystem.GetFilesAsync(directory, "*.txt");

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().HaveCount(2);
            result.Value.Should().Contain("/test/file1.txt");
            result.Value.Should().Contain("/test/file2.txt");
            result.Value.Should().NotContain("/test/file3.log");
        }

        [Fact(DisplayName = "IMFS-013: GetDirectoriesAsync returns correct directory list")]
        public async Task IMFS013()
        {
            // Arrange
            var baseDirectory = "/test";
            await _fileSystem.CreateDirectoryAsync(baseDirectory);
            await _fileSystem.CreateDirectoryAsync("/test/subdir1");
            await _fileSystem.CreateDirectoryAsync("/test/subdir2");
            await _fileSystem.CreateDirectoryAsync("/test/other");

            // Act
            var result = await _fileSystem.GetDirectoriesAsync(baseDirectory, "subdir*");

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().HaveCount(2);
            result.Value.Should().Contain("/test/subdir1");
            result.Value.Should().Contain("/test/subdir2");
            result.Value.Should().NotContain("/test/other");
        }

        [Fact(DisplayName = "IMFS-014: DeleteFileAsync removes file successfully")]
        public async Task IMFS014()
        {
            // Arrange
            var path = "/test/file.txt";
            await _fileSystem.WriteAllTextAsync(path, "content");

            // Act
            var result = await _fileSystem.DeleteFileAsync(path);

            // Assert
            result.IsSuccess.Should().BeTrue();
            
            var existsResult = await _fileSystem.FileExistsAsync(path);
            existsResult.IsSuccess.Should().BeTrue();
            existsResult.Value.Should().BeFalse();
        }

        [Fact(DisplayName = "IMFS-015: DeleteDirectoryAsync removes empty directory")]
        public async Task IMFS015()
        {
            // Arrange
            var path = "/test/directory";
            await _fileSystem.CreateDirectoryAsync(path);

            // Act
            var result = await _fileSystem.DeleteDirectoryAsync(path);

            // Assert
            result.IsSuccess.Should().BeTrue();
            
            var existsResult = await _fileSystem.DirectoryExistsAsync(path);
            existsResult.IsSuccess.Should().BeTrue();
            existsResult.Value.Should().BeFalse();
        }

        [Fact(DisplayName = "IMFS-016: DeleteDirectoryAsync with recursive removes all contents")]
        public async Task IMFS016()
        {
            // Arrange
            var path = "/test/directory";
            await _fileSystem.CreateDirectoryAsync(path);
            await _fileSystem.CreateDirectoryAsync("/test/directory/subdir");
            await _fileSystem.WriteAllTextAsync("/test/directory/file.txt", "content");
            await _fileSystem.WriteAllTextAsync("/test/directory/subdir/file2.txt", "content2");

            // Act
            var result = await _fileSystem.DeleteDirectoryAsync(path, recursive: true);

            // Assert
            result.IsSuccess.Should().BeTrue();
            
            var existsResult = await _fileSystem.DirectoryExistsAsync(path);
            existsResult.IsSuccess.Should().BeTrue();
            existsResult.Value.Should().BeFalse();
            
            var fileExistsResult = await _fileSystem.FileExistsAsync("/test/directory/file.txt");
            fileExistsResult.Value.Should().BeFalse();
            
            var subdirExistsResult = await _fileSystem.DirectoryExistsAsync("/test/directory/subdir");
            subdirExistsResult.Value.Should().BeFalse();
        }

        [Fact(DisplayName = "IMFS-017: EnumerateFileSystemEntriesAsync yields correct entries")]
        public async Task IMFS017()
        {
            // Arrange
            var directory = "/test";
            await _fileSystem.CreateDirectoryAsync(directory);
            await _fileSystem.CreateDirectoryAsync("/test/subdir");
            await _fileSystem.WriteAllTextAsync("/test/file1.txt", "content1");
            await _fileSystem.WriteAllTextAsync("/test/file2.txt", "content2");

            // Act
            var entries = new List<FileSystemEntry>();
            await foreach (var result in _fileSystem.EnumerateFileSystemEntriesAsync(directory))
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

        [Fact(DisplayName = "IMFS-018: CreateMonitorAsync returns functional monitor")]
        public async Task IMFS018()
        {
            // Arrange & Act
            var monitor = await _fileSystem.CreateMonitorAsync();

            // Assert
            monitor.Should().NotBeNull();
            monitor.Should().BeAssignableTo<IFileSystemMonitor>();
        }

        [Fact(DisplayName = "IMFS-019: Path normalization works correctly")]
        public async Task IMFS019()
        {
            // Arrange
            var content = "test content";

            // Act
            await _fileSystem.WriteAllTextAsync("\\test\\file.txt", content);
            await _fileSystem.WriteAllTextAsync("/test//file2.txt", content);
            await _fileSystem.WriteAllTextAsync("/test/./file3.txt", content);

            // Assert
            var result1 = await _fileSystem.FileExistsAsync("/test/file.txt");
            result1.Value.Should().BeTrue();

            var result2 = await _fileSystem.FileExistsAsync("/test/file2.txt");
            result2.Value.Should().BeTrue();

            var result3 = await _fileSystem.FileExistsAsync("/test/file3.txt");
            result3.Value.Should().BeTrue();
        }

        [Fact(DisplayName = "IMFS-020: Parent directories are created automatically")]
        public async Task IMFS020()
        {
            // Arrange
            var path = "/deep/nested/directory/file.txt";
            var content = "test content";

            // Act
            await _fileSystem.WriteAllTextAsync(path, content);

            // Assert
            var dirExists1 = await _fileSystem.DirectoryExistsAsync("/deep");
            dirExists1.Value.Should().BeTrue();

            var dirExists2 = await _fileSystem.DirectoryExistsAsync("/deep/nested");
            dirExists2.Value.Should().BeTrue();

            var dirExists3 = await _fileSystem.DirectoryExistsAsync("/deep/nested/directory");
            dirExists3.Value.Should().BeTrue();

            var fileExists = await _fileSystem.FileExistsAsync(path);
            fileExists.Value.Should().BeTrue();
        }

        public void Dispose()
        {
            _fileSystem?.Dispose();
        }
    }
}
using System;
using System.IO;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;
using FluentAssertions;
using UnitTests.Extensions.FileSystem.Local.TestHelpers;
using Xunit;

namespace UnitTests.Extensions.FileSystem.Local
{
    public class LocalFileSystemMoveTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly LocalFileSystem _fileSystem;
        private readonly IPathMapper _pathMapper;
        
        public LocalFileSystemMoveTests()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), $"FSTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDirectory);
            
            _pathMapper = new TestPathMapper();
            _fileSystem = new LocalFileSystem(_pathMapper);
        }
        
        [Fact(DisplayName = "MOVE-001: MoveFileAsync should move file when destination does not exist")]
        public async Task MOVE001()
        {
            // Arrange
            var sourceFile = Path.Combine(_testDirectory, "source.txt");
            var destFile = Path.Combine(_testDirectory, "dest.txt");
            var content = "test content";
            
            File.WriteAllText(sourceFile, content);
            
            var sourcePath = CanonicalPath.Create(sourceFile).Value;
            var destPath = CanonicalPath.Create(destFile).Value;
            
            // Act
            var result = await _fileSystem.MoveFileAsync(sourcePath, destPath);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            File.Exists(sourceFile).Should().BeFalse();
            File.Exists(destFile).Should().BeTrue();
            File.ReadAllText(destFile).Should().Be(content);
        }
        
        [Fact(DisplayName = "MOVE-002: MoveFileAsync should fail when source does not exist")]
        public async Task MOVE002()
        {
            // Arrange
            var sourceFile = Path.Combine(_testDirectory, "nonexistent.txt");
            var destFile = Path.Combine(_testDirectory, "dest.txt");
            
            var sourcePath = CanonicalPath.Create(sourceFile).Value;
            var destPath = CanonicalPath.Create(destFile).Value;
            
            // Act
            var result = await _fileSystem.MoveFileAsync(sourcePath, destPath);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Type.Should().Be(FileSystemErrorType.FileNotFound);
        }
        
        [Fact(DisplayName = "MOVE-003: MoveFileAsync should fail when destination exists and overwrite is false")]
        public async Task MOVE003()
        {
            // Arrange
            var sourceFile = Path.Combine(_testDirectory, "source.txt");
            var destFile = Path.Combine(_testDirectory, "dest.txt");
            
            File.WriteAllText(sourceFile, "source content");
            File.WriteAllText(destFile, "dest content");
            
            var sourcePath = CanonicalPath.Create(sourceFile).Value;
            var destPath = CanonicalPath.Create(destFile).Value;
            
            // Act
            var result = await _fileSystem.MoveFileAsync(sourcePath, destPath, overwrite: false);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Type.Should().Be(FileSystemErrorType.FileAlreadyExists);
            File.Exists(sourceFile).Should().BeTrue(); // Source should still exist
            File.ReadAllText(destFile).Should().Be("dest content"); // Dest unchanged
        }
        
        [Fact(DisplayName = "MOVE-004: MoveFileAsync should overwrite when destination exists and overwrite is true")]
        public async Task MOVE004()
        {
            // Arrange
            var sourceFile = Path.Combine(_testDirectory, "source.txt");
            var destFile = Path.Combine(_testDirectory, "dest.txt");
            var sourceContent = "source content";
            
            File.WriteAllText(sourceFile, sourceContent);
            File.WriteAllText(destFile, "dest content");
            
            var sourcePath = CanonicalPath.Create(sourceFile).Value;
            var destPath = CanonicalPath.Create(destFile).Value;
            
            // Act
            var result = await _fileSystem.MoveFileAsync(sourcePath, destPath, overwrite: true);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            File.Exists(sourceFile).Should().BeFalse();
            File.Exists(destFile).Should().BeTrue();
            File.ReadAllText(destFile).Should().Be(sourceContent);
        }
        
        [Fact(DisplayName = "MOVE-005: MoveFileAsync should preserve attributes")]
        public async Task MOVE005()
        {
            // Arrange
            var sourceFile = Path.Combine(_testDirectory, "source.txt");
            var destFile = Path.Combine(_testDirectory, "dest.txt");
            
            File.WriteAllText(sourceFile, "content");
            
            var sourceInfo = new System.IO.FileInfo(sourceFile);
            sourceInfo.Attributes |= FileAttributes.ReadOnly;
            var originalCreationTime = sourceInfo.CreationTimeUtc;
            
            var sourcePath = CanonicalPath.Create(sourceFile).Value;
            var destPath = CanonicalPath.Create(destFile).Value;
            
            // Act
            var result = await _fileSystem.MoveFileAsync(sourcePath, destPath);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            var destInfo = new System.IO.FileInfo(destFile);
            (destInfo.Attributes & FileAttributes.ReadOnly).Should().NotBe(FileAttributes.Normal);
            destInfo.CreationTimeUtc.Should().Be(originalCreationTime);
        }

        [Fact(DisplayName = "MOVE-006: MoveFileAsync should handle same source and destination")]
        public async Task MOVE006()
        {
            // Arrange
            var sourceFile = Path.Combine(_testDirectory, "source.txt");
            File.WriteAllText(sourceFile, "content");
            
            var path = CanonicalPath.Create(sourceFile).Value;
            
            // Act
            var result = await _fileSystem.MoveFileAsync(path, path);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Type.Should().Be(FileSystemErrorType.InvalidPath);
        }

        [Fact(DisplayName = "MOVE-007: MoveFileAsync should create destination directory")]
        public async Task MOVE007()
        {
            // Arrange
            var sourceFile = Path.Combine(_testDirectory, "source.txt");
            var destFile = Path.Combine(_testDirectory, "subdir", "dest.txt");
            var content = "test content";
            
            File.WriteAllText(sourceFile, content);
            
            var sourcePath = CanonicalPath.Create(sourceFile).Value;
            var destPath = CanonicalPath.Create(destFile).Value;
            
            // Act
            var result = await _fileSystem.MoveFileAsync(sourcePath, destPath);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            Directory.Exists(Path.Combine(_testDirectory, "subdir")).Should().BeTrue();
            File.Exists(destFile).Should().BeTrue();
            File.ReadAllText(destFile).Should().Be(content);
        }
        
        public void Dispose()
        {
            try
            {
                // Clear readonly attributes to allow deletion
                foreach (var file in Directory.GetFiles(_testDirectory, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(_testDirectory, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }
}
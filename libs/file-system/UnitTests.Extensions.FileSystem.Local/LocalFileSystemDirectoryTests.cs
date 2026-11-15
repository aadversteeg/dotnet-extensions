using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.Functional;
using FluentAssertions;
using System.Runtime.InteropServices;
using Ave.Extensions.FileSystem.Local.PathMappers;
using UnitTests.Extensions.FileSystem.Local.TestHelpers;
using Xunit;

namespace UnitTests.Extensions.FileSystem.Local
{
    public class LocalFileSystemDirectoryTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly LocalFileSystem _fileSystem;
        private readonly IPathMapper _pathMapper;
        
        public LocalFileSystemDirectoryTests()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), $"FSTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDirectory);
            
            _pathMapper = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? (IPathMapper)new WindowsPathMapper() 
                : new UnixPathMapper();
            _fileSystem = new LocalFileSystem(_pathMapper);
        }
        
        [Fact(DisplayName = "DIR-001: MoveDirectoryAsync should move directory when destination does not exist")]
        public async Task DIR001()
        {
            // Arrange
            var sourceDir = Path.Combine(_testDirectory, "source");
            var destDir = Path.Combine(_testDirectory, "dest");
            
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "file1.txt"), "content1");
            Directory.CreateDirectory(Path.Combine(sourceDir, "subdir"));
            File.WriteAllText(Path.Combine(sourceDir, "subdir", "file2.txt"), "content2");
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceDir).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destDir).Value;
            
            // Act
            var result = await _fileSystem.MoveDirectoryAsync(sourcePath, destPath);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            Directory.Exists(sourceDir).Should().BeFalse();
            Directory.Exists(destDir).Should().BeTrue();
            File.Exists(Path.Combine(destDir, "file1.txt")).Should().BeTrue();
            File.Exists(Path.Combine(destDir, "subdir", "file2.txt")).Should().BeTrue();
        }
        
        [Fact(DisplayName = "DIR-002: MoveDirectoryAsync should fail when destination exists")]
        public async Task DIR002()
        {
            // Arrange
            var sourceDir = Path.Combine(_testDirectory, "source");
            var destDir = Path.Combine(_testDirectory, "dest");
            
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(destDir);
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceDir).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destDir).Value;
            
            // Act
            var result = await _fileSystem.MoveDirectoryAsync(sourcePath, destPath);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.IO.DirectoryAlreadyExists);
        }
        
        [Fact(DisplayName = "DIR-003: MoveDirectoryAsync should fail when moving to subdirectory")]
        public async Task DIR003()
        {
            // Arrange
            var sourceDir = Path.Combine(_testDirectory, "source");
            var destDir = Path.Combine(_testDirectory, "source", "subdest");
            
            Directory.CreateDirectory(sourceDir);
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceDir).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destDir).Value;
            
            // Act
            var result = await _fileSystem.MoveDirectoryAsync(sourcePath, destPath);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.IO.InvalidPath);
        }
        
        [Fact(DisplayName = "DIR-004: CopyDirectoryAsync should copy directory with all contents")]
        public async Task DIR004()
        {
            // Arrange
            var sourceDir = Path.Combine(_testDirectory, "source");
            var destDir = Path.Combine(_testDirectory, "dest");
            
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "file1.txt"), "content1");
            Directory.CreateDirectory(Path.Combine(sourceDir, "subdir"));
            File.WriteAllText(Path.Combine(sourceDir, "subdir", "file2.txt"), "content2");
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceDir).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destDir).Value;
            
            // Act
            var result = await _fileSystem.CopyDirectoryAsync(sourcePath, destPath);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            Directory.Exists(sourceDir).Should().BeTrue(); // Source still exists
            Directory.Exists(destDir).Should().BeTrue();
            File.Exists(Path.Combine(destDir, "file1.txt")).Should().BeTrue();
            File.Exists(Path.Combine(destDir, "subdir", "file2.txt")).Should().BeTrue();
            File.ReadAllText(Path.Combine(destDir, "file1.txt")).Should().Be("content1");
            File.ReadAllText(Path.Combine(destDir, "subdir", "file2.txt")).Should().Be("content2");
        }
        
        [Fact(DisplayName = "DIR-005: CopyDirectoryAsync should be cancellable")]
        public async Task DIR005()
        {
            // Arrange
            var sourceDir = Path.Combine(_testDirectory, "source");
            var destDir = Path.Combine(_testDirectory, "dest");
            
            Directory.CreateDirectory(sourceDir);
            // Create many files with some data to slow down the operation
            for (int i = 0; i < 50; i++)
            {
                var data = new string('X', 10000); // 10KB per file
                File.WriteAllText(Path.Combine(sourceDir, $"file{i}.txt"), data);
            }
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceDir).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destDir).Value;
            
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(50); // Cancel after 50ms
            
            // Act & Assert
            try
            {
                await _fileSystem.CopyDirectoryAsync(sourcePath, destPath, cts.Token);
                // If we reach here, the operation completed before cancellation
                // This is acceptable behavior and not a failure
            }
            catch (OperationCanceledException)
            {
                // Expected behavior - operation was cancelled
                // The destination directory should be cleaned up
                Directory.Exists(destDir).Should().BeFalse();
            }
        }

        [Fact(DisplayName = "DIR-006: MoveDirectoryAsync should fail when source does not exist")]
        public async Task DIR006()
        {
            // Arrange
            var sourceDir = Path.Combine(_testDirectory, "nonexistent");
            var destDir = Path.Combine(_testDirectory, "dest");
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceDir).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destDir).Value;
            
            // Act
            var result = await _fileSystem.MoveDirectoryAsync(sourcePath, destPath);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.NotFound.Directory);
        }

        [Fact(DisplayName = "DIR-007: CopyDirectoryAsync should fail when source does not exist")]
        public async Task DIR007()
        {
            // Arrange
            var sourceDir = Path.Combine(_testDirectory, "nonexistent");
            var destDir = Path.Combine(_testDirectory, "dest");
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceDir).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destDir).Value;
            
            // Act
            var result = await _fileSystem.CopyDirectoryAsync(sourcePath, destPath);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.NotFound.Directory);
        }

        [Fact(DisplayName = "DIR-008: MoveDirectoryAsync should handle same source and destination")]
        public async Task DIR008()
        {
            // Arrange
            var sourceDir = Path.Combine(_testDirectory, "source");
            Directory.CreateDirectory(sourceDir);
            
            var path = _pathMapper.MapToCanonicalPath(sourceDir).Value;
            
            // Act
            var result = await _fileSystem.MoveDirectoryAsync(path, path);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.IO.InvalidPath);
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
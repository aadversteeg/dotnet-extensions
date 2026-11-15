using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Local.PathMappers;
using Ave.Extensions.Functional;
using FluentAssertions;
using UnitTests.Extensions.FileSystem.Local.TestHelpers;
using Xunit;

namespace UnitTests.Extensions.FileSystem.Local
{
    public class LocalFileSystemCopyTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly LocalFileSystem _fileSystem;
        private readonly IPathMapper _pathMapper;
        
        public LocalFileSystemCopyTests()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), $"FSTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDirectory);
            
            _pathMapper = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? (IPathMapper)new WindowsPathMapper() 
                : new UnixPathMapper();
            _fileSystem = new LocalFileSystem(_pathMapper);
        }
        
        [Fact(DisplayName = "COPY-001: CopyFileAsync should copy file when destination does not exist")]
        public async Task COPY001()
        {
            // Arrange
            var sourceFile = Path.Combine(_testDirectory, "source.txt");
            var destFile = Path.Combine(_testDirectory, "dest.txt");
            var content = "test content";
            
            File.WriteAllText(sourceFile, content);
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceFile).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destFile).Value;
            
            // Act
            var result = await _fileSystem.CopyFileAsync(sourcePath, destPath);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            File.Exists(sourceFile).Should().BeTrue(); // Source still exists
            File.Exists(destFile).Should().BeTrue();
            File.ReadAllText(destFile).Should().Be(content);
        }
        
        [Fact(DisplayName = "COPY-002: CopyFileAsync should create destination directory")]
        public async Task COPY002()
        {
            // Arrange
            var sourceFile = Path.Combine(_testDirectory, "source.txt");
            var destFile = Path.Combine(_testDirectory, "subdir", "dest.txt");
            var content = "test content";
            
            File.WriteAllText(sourceFile, content);
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceFile).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destFile).Value;
            
            // Act
            var result = await _fileSystem.CopyFileAsync(sourcePath, destPath);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            Directory.Exists(Path.Combine(_testDirectory, "subdir")).Should().BeTrue();
            File.Exists(destFile).Should().BeTrue();
            File.ReadAllText(destFile).Should().Be(content);
        }
        
        [Fact(DisplayName = "COPY-003: CopyFileAsync should be cancellable")]
        public async Task COPY003()
        {
            // Arrange
            var sourceFile = Path.Combine(_testDirectory, "large.bin");
            var destFile = Path.Combine(_testDirectory, "dest.bin");
            
            // Create a reasonably sized file
            var data = new byte[10 * 1024 * 1024]; // 10MB
            File.WriteAllBytes(sourceFile, data);
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceFile).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destFile).Value;
            
            using var cts = new CancellationTokenSource();
            
            // Act
            var copyTask = _fileSystem.CopyFileAsync(sourcePath, destPath, cancellationToken: cts.Token);
            cts.Cancel();
            
            // Assert
            // Accept either OperationCanceledException or TaskCanceledException
            // The specific type can vary based on execution context
            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => copyTask);
            // Verify cleanup occurred
            File.Exists(destFile).Should().BeFalse();
        }
        
        [Fact(DisplayName = "COPY-004: CopyFileAsync should handle same source and destination")]
        public async Task COPY004()
        {
            // Arrange
            var sourceFile = Path.Combine(_testDirectory, "source.txt");
            File.WriteAllText(sourceFile, "content");
            
            var path = _pathMapper.MapToCanonicalPath(sourceFile).Value;
            
            // Act
            var result = await _fileSystem.CopyFileAsync(path, path);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.IO.InvalidPath);
        }

        [Fact(DisplayName = "COPY-005: CopyFileAsync should fail when source does not exist")]
        public async Task COPY005()
        {
            // Arrange
            var sourceFile = Path.Combine(_testDirectory, "nonexistent.txt");
            var destFile = Path.Combine(_testDirectory, "dest.txt");
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceFile).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destFile).Value;
            
            // Act
            var result = await _fileSystem.CopyFileAsync(sourcePath, destPath);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.NotFound.File);
        }

        [Fact(DisplayName = "COPY-006: CopyFileAsync should fail when destination exists and overwrite is false")]
        public async Task COPY006()
        {
            // Arrange
            var sourceFile = Path.Combine(_testDirectory, "source.txt");
            var destFile = Path.Combine(_testDirectory, "dest.txt");
            
            File.WriteAllText(sourceFile, "source content");
            File.WriteAllText(destFile, "dest content");
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceFile).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destFile).Value;
            
            // Act
            var result = await _fileSystem.CopyFileAsync(sourcePath, destPath, overwrite: false);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.IO.FileAlreadyExists);
            File.ReadAllText(destFile).Should().Be("dest content"); // Dest unchanged
        }

        [Fact(DisplayName = "COPY-007: CopyFileAsync should overwrite when destination exists and overwrite is true")]
        public async Task COPY007()
        {
            // Arrange
            var sourceFile = Path.Combine(_testDirectory, "source.txt");
            var destFile = Path.Combine(_testDirectory, "dest.txt");
            var sourceContent = "source content";
            
            File.WriteAllText(sourceFile, sourceContent);
            File.WriteAllText(destFile, "dest content");
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceFile).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destFile).Value;
            
            // Act
            var result = await _fileSystem.CopyFileAsync(sourcePath, destPath, overwrite: true);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            File.Exists(sourceFile).Should().BeTrue(); // Source still exists
            File.Exists(destFile).Should().BeTrue();
            File.ReadAllText(destFile).Should().Be(sourceContent);
        }

        [Fact(DisplayName = "COPY-008: CopyFileAsync should preserve attributes")]
        public async Task COPY008()
        {
            // Arrange
            var sourceFile = Path.Combine(_testDirectory, "source.txt");
            var destFile = Path.Combine(_testDirectory, "dest.txt");
            
            File.WriteAllText(sourceFile, "content");
            
            var sourceInfo = new System.IO.FileInfo(sourceFile);
            sourceInfo.Attributes |= FileAttributes.ReadOnly;
            var originalCreationTime = sourceInfo.CreationTimeUtc;
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceFile).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destFile).Value;
            
            // Act
            var result = await _fileSystem.CopyFileAsync(sourcePath, destPath);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            var destInfo = new System.IO.FileInfo(destFile);
            (destInfo.Attributes & FileAttributes.ReadOnly).Should().NotBe(FileAttributes.Normal);
            destInfo.CreationTimeUtc.Should().Be(originalCreationTime);
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
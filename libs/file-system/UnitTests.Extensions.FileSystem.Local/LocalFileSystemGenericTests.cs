using System;
using System.IO;
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
    public class LocalFileSystemGenericTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly LocalFileSystem _fileSystem;
        private readonly IPathMapper _pathMapper;
        
        public LocalFileSystemGenericTests()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), $"FSTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDirectory);
            
            _pathMapper = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? (IPathMapper)new WindowsPathMapper() 
                : new UnixPathMapper();
            _fileSystem = new LocalFileSystem(_pathMapper);
        }
        
        [Fact(DisplayName = "GEN-001: MoveAsync should detect and move file")]
        public async Task GEN001()
        {
            // Arrange
            var sourceFile = Path.Combine(_testDirectory, "source.txt");
            var destFile = Path.Combine(_testDirectory, "dest.txt");
            File.WriteAllText(sourceFile, "content");
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceFile).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destFile).Value;
            
            // Act
            var result = await _fileSystem.MoveAsync(sourcePath, destPath);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            File.Exists(sourceFile).Should().BeFalse();
            File.Exists(destFile).Should().BeTrue();
        }
        
        [Fact(DisplayName = "GEN-002: MoveAsync should detect and move directory")]
        public async Task GEN002()
        {
            // Arrange
            var sourceDir = Path.Combine(_testDirectory, "source");
            var destDir = Path.Combine(_testDirectory, "dest");
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "file.txt"), "content");
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceDir).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destDir).Value;
            
            // Act
            var result = await _fileSystem.MoveAsync(sourcePath, destPath);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            Directory.Exists(sourceDir).Should().BeFalse();
            Directory.Exists(destDir).Should().BeTrue();
            File.Exists(Path.Combine(destDir, "file.txt")).Should().BeTrue();
        }
        
        [Fact(DisplayName = "GEN-003: MoveAsync should respect overwrite for files")]
        public async Task GEN003()
        {
            // Arrange
            var sourceFile = Path.Combine(_testDirectory, "source.txt");
            var destFile = Path.Combine(_testDirectory, "dest.txt");
            File.WriteAllText(sourceFile, "source content");
            File.WriteAllText(destFile, "dest content");
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceFile).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destFile).Value;
            
            // Act - without overwrite
            var result1 = await _fileSystem.MoveAsync(sourcePath, destPath, overwrite: false);
            
            // Assert
            result1.IsFailure.Should().BeTrue();
            result1.Error.Code.Should().Be(FileSystemErrorCodes.IO.FileAlreadyExists);
            
            // Act - with overwrite
            var result2 = await _fileSystem.MoveAsync(sourcePath, destPath, overwrite: true);
            
            // Assert
            result2.IsSuccess.Should().BeTrue();
            File.Exists(sourceFile).Should().BeFalse();
            File.ReadAllText(destFile).Should().Be("source content");
        }
        
        [Fact(DisplayName = "GEN-004: CopyAsync should detect and copy file")]
        public async Task GEN004()
        {
            // Arrange
            var sourceFile = Path.Combine(_testDirectory, "source.txt");
            var destFile = Path.Combine(_testDirectory, "dest.txt");
            File.WriteAllText(sourceFile, "content");
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceFile).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destFile).Value;
            
            // Act
            var result = await _fileSystem.CopyAsync(sourcePath, destPath);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            File.Exists(sourceFile).Should().BeTrue();
            File.Exists(destFile).Should().BeTrue();
            File.ReadAllText(destFile).Should().Be("content");
        }
        
        [Fact(DisplayName = "GEN-005: CopyAsync should detect and copy directory")]
        public async Task GEN005()
        {
            // Arrange
            var sourceDir = Path.Combine(_testDirectory, "source");
            var destDir = Path.Combine(_testDirectory, "dest");
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "file.txt"), "content");
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceDir).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destDir).Value;
            
            // Act
            var result = await _fileSystem.CopyAsync(sourcePath, destPath);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            Directory.Exists(sourceDir).Should().BeTrue();
            Directory.Exists(destDir).Should().BeTrue();
            File.Exists(Path.Combine(destDir, "file.txt")).Should().BeTrue();
        }
        
        [Fact(DisplayName = "GEN-006: Generic methods should fail when source does not exist")]
        public async Task GEN006()
        {
            // Arrange
            var sourcePath = _pathMapper.MapToCanonicalPath(Path.Combine(_testDirectory, "nonexistent")).Value;
            var destPath = _pathMapper.MapToCanonicalPath(Path.Combine(_testDirectory, "dest")).Value;
            
            // Act
            var moveResult = await _fileSystem.MoveAsync(sourcePath, destPath);
            var copyResult = await _fileSystem.CopyAsync(sourcePath, destPath);
            
            // Assert
            moveResult.IsFailure.Should().BeTrue();
            moveResult.Error.Code.Should().Be(FileSystemErrorCodes.NotFound.Path);
            copyResult.IsFailure.Should().BeTrue();
            copyResult.Error.Code.Should().Be(FileSystemErrorCodes.NotFound.Path);
        }

        [Fact(DisplayName = "GEN-007: MoveAsync should reject overwrite for directories")]
        public async Task GEN007()
        {
            // Arrange
            var sourceDir = Path.Combine(_testDirectory, "source");
            var destDir = Path.Combine(_testDirectory, "dest");
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(destDir);
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceDir).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destDir).Value;
            
            // Act
            var result = await _fileSystem.MoveAsync(sourcePath, destPath, overwrite: true);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.IO.DirectoryAlreadyExists);
        }

        [Fact(DisplayName = "GEN-008: CopyAsync should reject overwrite for directories")]
        public async Task GEN008()
        {
            // Arrange
            var sourceDir = Path.Combine(_testDirectory, "source");
            var destDir = Path.Combine(_testDirectory, "dest");
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(destDir);
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceDir).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destDir).Value;
            
            // Act
            var result = await _fileSystem.CopyAsync(sourcePath, destPath, overwrite: true);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.IO.DirectoryAlreadyExists);
        }

        [Fact(DisplayName = "GEN-009: CopyAsync should respect overwrite for files")]
        public async Task GEN009()
        {
            // Arrange
            var sourceFile = Path.Combine(_testDirectory, "source.txt");
            var destFile = Path.Combine(_testDirectory, "dest.txt");
            File.WriteAllText(sourceFile, "source content");
            File.WriteAllText(destFile, "dest content");
            
            var sourcePath = _pathMapper.MapToCanonicalPath(sourceFile).Value;
            var destPath = _pathMapper.MapToCanonicalPath(destFile).Value;
            
            // Act - without overwrite
            var result1 = await _fileSystem.CopyAsync(sourcePath, destPath, overwrite: false);
            
            // Assert
            result1.IsFailure.Should().BeTrue();
            result1.Error.Code.Should().Be(FileSystemErrorCodes.IO.FileAlreadyExists);
            
            // Act - with overwrite
            var result2 = await _fileSystem.CopyAsync(sourcePath, destPath, overwrite: true);
            
            // Assert
            result2.IsSuccess.Should().BeTrue();
            File.Exists(sourceFile).Should().BeTrue(); // Source still exists
            File.ReadAllText(destFile).Should().Be("source content");
        }
        
        public void Dispose()
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }
}
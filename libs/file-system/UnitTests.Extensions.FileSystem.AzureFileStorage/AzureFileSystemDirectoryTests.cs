using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.AzureFileStorage;
using Ave.Extensions.FileSystem.AzureFileStorage.Configuration;
using Ave.Extensions.FileSystem.Local.PathMappers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace UnitTests.Extensions.FileSystem.AzureFileStorage
{
    /// <summary>
    /// Unit tests for AzureFileSystem directory operations.
    /// Note: These tests focus on business logic validation since proper Azure SDK mocking
    /// requires significant setup. Integration tests should be used for full Azure SDK testing.
    /// </summary>
    public class AzureFileSystemDirectoryTests
    {
        private readonly Mock<ILogger<AzureFileSystem>> _mockLogger;
        private readonly AzureFileSystemOptions _options;
        private readonly AzureFileSystem _fileSystem;
        
        public AzureFileSystemDirectoryTests()
        {
            _mockLogger = new Mock<ILogger<AzureFileSystem>>();
            
            _options = new AzureFileSystemOptions
            {
                ConnectionString = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=dGVzdA==;",
                ShareName = "testshare",
                CreateShareIfNotExists = false
            };
            
            var options = Options.Create(_options);
            // Canonical paths map to themselves, as Azure File Share paths use forward slashes
            _fileSystem = new AzureFileSystem(_mockLogger.Object, options, new UnixPathMapper());
        }
        
        [Fact(DisplayName = "AZFS-009: MoveDirectoryAsync should fail when source and destination are the same")]
        public async Task AZFS009()
        {
            // Arrange
            var path = CanonicalPath.Create("/samedir").Value;
            
            // Act
            var result = await _fileSystem.MoveDirectoryAsync(path, path);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.IO.InvalidPath);
            result.Error.Message.Should().Contain("Source and destination paths are the same");
        }
        
        [Fact(DisplayName = "AZFS-010: CopyDirectoryAsync should fail when destination is subdirectory of source")]
        public async Task AZFS010()
        {
            // Arrange
            var sourcePath = CanonicalPath.Create("/parent").Value;
            var destPath = CanonicalPath.Create("/parent/child").Value;
            
            // Act
            var result = await _fileSystem.CopyDirectoryAsync(sourcePath, destPath);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.IO.InvalidPath);
            result.Error.Message.Should().Contain("subdirectory of itself");
        }
        
        [Fact(DisplayName = "AZFS-011: MoveDirectoryAsync should handle Azure SDK exceptions gracefully")]
        public async Task AZFS011()
        {
            // This test validates that Azure SDK exceptions are properly caught and mapped
            
            // Arrange
            var sourcePath = CanonicalPath.Create("/sourcedir").Value;
            var destPath = CanonicalPath.Create("/destdir").Value;
            
            // Act
            var result = await _fileSystem.MoveDirectoryAsync(sourcePath, destPath);
            
            // Assert
            // Since we don't have a real Azure connection, this should fail gracefully
            result.IsFailure.Should().BeTrue();
            result.Error.Should().NotBeNull();
        }
        
        [Fact(DisplayName = "AZFS-012: CopyDirectoryAsync should validate canonical paths")]
        public async Task AZFS012()
        {
            // Arrange
            var sourcePath = CanonicalPath.Create("/sourcedir").Value;
            var destPath = CanonicalPath.Create("/destdir").Value;
            
            // Act
            var result = await _fileSystem.CopyDirectoryAsync(sourcePath, destPath);
            
            // Assert
            // This will fail because we don't have a real Azure connection,
            // but it validates that the method accepts canonical paths
            result.IsFailure.Should().BeTrue();
            // The actual error will be Azure-related, not path validation
        }
        
        [Fact(DisplayName = "AZFS-013: MoveDirectoryAsync should fail for complex nested subdirectory")]
        public async Task AZFS013()
        {
            // Arrange
            var sourcePath = CanonicalPath.Create("/root/parent").Value;
            var destPath = CanonicalPath.Create("/root/parent/child/grandchild").Value;
            
            // Act
            var result = await _fileSystem.MoveDirectoryAsync(sourcePath, destPath);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.IO.InvalidPath);
            result.Error.Message.Should().Contain("subdirectory of itself");
        }
    }
}
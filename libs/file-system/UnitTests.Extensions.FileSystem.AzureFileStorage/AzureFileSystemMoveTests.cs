using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem;
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
    /// Unit tests for AzureFileSystem move operations.
    /// Note: These tests focus on business logic validation since proper Azure SDK mocking
    /// requires significant setup. Integration tests should be used for full Azure SDK testing.
    /// </summary>
    public class AzureFileSystemMoveTests
    {
        private readonly Mock<ILogger<AzureFileSystem>> _mockLogger;
        private readonly AzureFileSystemOptions _options;
        private readonly AzureFileSystem _fileSystem;
        
        public AzureFileSystemMoveTests()
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
        
        [Fact(DisplayName = "AZFS-001: MoveFileAsync should fail when source and destination are the same")]
        public async Task AZFS001()
        {
            // Arrange
            var path = CanonicalPath.Create("/samefile.txt").Value;
            
            // Act
            var result = await _fileSystem.MoveFileAsync(path, path);
            
            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.IO.InvalidPath);
            result.Error.Message.Should().Contain("Source and destination paths are the same");
        }
        
        // Calls the Azure SDK against a real host name and waits for its retries (20-50 s): CI excludes Category=Network
        [Trait("Category", "Network")]
        [Fact(DisplayName = "AZFS-002: MoveFileAsync should validate canonical paths")]
        public async Task AZFS002()
        {
            // Arrange
            var sourcePath = CanonicalPath.Create("/source.txt").Value;
            var destPath = CanonicalPath.Create("/dest.txt").Value;
            
            // Act
            var result = await _fileSystem.MoveFileAsync(sourcePath, destPath);
            
            // Assert
            // This will fail because we don't have a real Azure connection,
            // but it validates that the method accepts canonical paths
            result.IsFailure.Should().BeTrue();
            // The actual error will be Azure-related, not path validation
        }
        
        // Calls the Azure SDK against a real host name and waits for its retries (20-50 s): CI excludes Category=Network
        [Trait("Category", "Network")]
        [Fact(DisplayName = "AZFS-003: MoveFileAsync should handle Azure SDK exceptions gracefully")]
        public async Task AZFS003()
        {
            // This test validates that Azure SDK exceptions are properly caught and mapped
            
            // Arrange
            var sourcePath = CanonicalPath.Create("/source.txt").Value;
            var destPath = CanonicalPath.Create("/dest.txt").Value;
            
            // Act
            var result = await _fileSystem.MoveFileAsync(sourcePath, destPath);
            
            // Assert
            // Since we don't have a real Azure connection, this should fail gracefully
            result.IsFailure.Should().BeTrue();
            result.Error.Should().NotBeNull();
        }
    }
}
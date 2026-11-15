using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Mapping;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using FluentAssertions;
using Moq;
using UnitTests.Extensions.FileSystem.Local.TestHelpers;

namespace UnitTests.Extensions.FileSystem.Local
{
    /// <summary>
    /// Unit tests for LocalFileSystem that focus on unique scenarios not covered by integration tests.
    /// Most LocalFileSystem functionality is comprehensively tested in the integration test suite.
    /// </summary>
    public class LocalFileSystemTests
    {
        [Fact(DisplayName = "LFS-001: CanonicalPath creation validates path format")]
        public void LFS001()
        {
            // Arrange
            var invalidPath = "";
            
            // Act
            // Test CanonicalPath validation logic, which is a prerequisite for LocalFileSystem operations
            var canonicalPathResult = CanonicalPath.Create(invalidPath);
            
            // Assert
            canonicalPathResult.IsFailure.Should().BeTrue();
            canonicalPathResult.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.PathNullOrEmpty);
        }

        [Fact(DisplayName = "LFS-002: Info property returns correct characteristics for LocalFileSystem")]
        public void LFS002()
        {
            // Arrange
            var mockPathMapper = new Mock<IPathMapper>();
            var fileSystem = new Ave.Extensions.FileSystem.Local.LocalFileSystem(mockPathMapper.Object);

            // Act
            var info = fileSystem.Info;

            // Assert
            info.IsDurable.Should().BeTrue("local file system persists data across application restarts");
            info.SupportsMonitoring.Should().BeTrue("local file system supports FileSystemWatcher for monitoring");
            info.IsReadOnly.Should().BeFalse("local file system supports both read and write operations");
        }

        [Fact(DisplayName = "LFS-003: TraverseAsync validates parameters")]
        public async Task LFS003()
        {
            // Arrange
            var mockPathMapper = new Mock<IPathMapper>();
            var fileSystem = new Ave.Extensions.FileSystem.Local.LocalFileSystem(mockPathMapper.Object);
            var mockHandler = new Mock<ITraversalHandler<FileSystemEntry>>();
            mockHandler.Setup(h => h.OnTraversalStartAsync(It.IsAny<CanonicalPath>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(Ave.Extensions.Functional.Result<Ave.Extensions.Functional.Unit, Error>.Success(Ave.Extensions.Functional.Unit.Value));

            // Act & Assert - no path mapper setup means path mapping fails
            var result = await fileSystem.TraverseAsync<FileSystemEntry>(
                "/nonexistent".ToCanonicalPath(),
                mockHandler.Object,
                IdentityMapper.Instance,
                null);

            // The result should indicate an invalid path error since the path mapper has no setup
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().BeOneOf(FileSystemErrorCodes.IO.InvalidPath, FileSystemErrorCodes.NotFound.Path, FileSystemErrorCodes.Unknown);
        }

        [Fact(DisplayName = "LFS-004: TraverseAsync uses default options when null")]
        public async Task LFS004()
        {
            // Arrange
            var mockPathMapper = new Mock<IPathMapper>();
            mockPathMapper.Setup(m => m.MapToSystemPath(It.IsAny<CanonicalPath>()))
                .Returns((CanonicalPath path) => Ave.Extensions.Functional.Result<string, Error>.Success(path.Value));

            var fileSystem = new Ave.Extensions.FileSystem.Local.LocalFileSystem(mockPathMapper.Object);
            var mockHandler = new Mock<ITraversalHandler<FileSystemEntry>>();
            mockHandler.Setup(h => h.OnTraversalStartAsync(It.IsAny<CanonicalPath>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(Ave.Extensions.Functional.Result<Ave.Extensions.Functional.Unit, Error>.Success(Ave.Extensions.Functional.Unit.Value));

            // Act - passing null options should use defaults
            var result = await fileSystem.TraverseAsync<FileSystemEntry>(
                "/nonexistent".ToCanonicalPath(),
                mockHandler.Object,
                IdentityMapper.Instance,
                null); // null options

            // Assert - should handle null options gracefully (error will be due to path not existing)
            result.IsFailure.Should().BeTrue();
            // The error should not be due to null options but due to path not existing
            result.Error.Code.Should().BeOneOf(FileSystemErrorCodes.NotFound.Directory, FileSystemErrorCodes.NotFound.Path, FileSystemErrorCodes.Unknown);
        }
    }
}
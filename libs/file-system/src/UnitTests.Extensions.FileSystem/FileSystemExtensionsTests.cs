using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.FileSystem.Traversal.Handlers;
using Ave.Extensions.Functional;
using FluentAssertions;
using Moq;
using UnitTests.Extensions.FileSystem.TestHelpers;

namespace UnitTests.Extensions.FileSystem
{
    public class FileSystemExtensionsTests
    {
        [Fact(DisplayName = "FSE-001: AsReadOnly returns read-only wrapper")]
        public void FSE001()
        {
            // Arrange
            var mockFileSystem = new Mock<Ave.Extensions.FileSystem.IFileSystem>();
            
            // Act
            var readOnlyFileSystem = mockFileSystem.Object.AsReadOnly();
            
            // Assert
            readOnlyFileSystem.Should().NotBeNull();
            readOnlyFileSystem.Should().BeAssignableTo<Ave.Extensions.FileSystem.IReadOnlyFileSystem>();
            
            // The wrapper shouldn't expose write operations
            var hasWriteOperations = 
                readOnlyFileSystem.GetType().GetMethod("WriteAllTextAsync") != null ||
                readOnlyFileSystem.GetType().GetMethod("DeleteFileAsync") != null ||
                readOnlyFileSystem.GetType().GetMethod("CreateDirectoryAsync") != null ||
                readOnlyFileSystem.GetType().GetMethod("DeleteDirectoryAsync") != null ||
                readOnlyFileSystem.GetType().GetMethod("OpenWriteAsync") != null ||
                readOnlyFileSystem.GetType().GetMethod("OpenAsync") != null;
                
            hasWriteOperations.Should().BeFalse();
        }
        
        [Fact(DisplayName = "FSE-002: AsReadOnly delegates methods correctly")]
        public async Task FSE002()
        {
            // Arrange
            var expectedResult = Result<bool, FileSystemError>.Success(true);
            var mockFileSystem = new Mock<Ave.Extensions.FileSystem.IFileSystem>();
            mockFileSystem.Setup(fs => fs.FileExistsAsync("/test.txt".ToCanonicalPath(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult);
            
            var readOnlyFileSystem = mockFileSystem.Object.AsReadOnly();
            
            // Act
            var result = await readOnlyFileSystem.FileExistsAsync("/test.txt".ToCanonicalPath());
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeTrue();
            mockFileSystem.Verify(fs => fs.FileExistsAsync("/test.txt".ToCanonicalPath(), It.IsAny<CancellationToken>()), Times.Once);
        }
        
        [Fact(DisplayName = "FSE-003: AsReadOnly returns same wrapper for already wrapped instance")]
        public void FSE003()
        {
            // Arrange
            var mockFileSystem = new Mock<Ave.Extensions.FileSystem.IFileSystem>();
            
            // Act - first wrap the file system
            var readOnlyFileSystem1 = mockFileSystem.Object.AsReadOnly();
            
            // Explicitly use the static extension method for the second wrapping
            var readOnlyFileSystem2 = FileSystemExtensions.AsReadOnly(readOnlyFileSystem1);
            
            // Assert
            // The second call should return the same wrapper instance (reference equals)
            ReferenceEquals(readOnlyFileSystem1, readOnlyFileSystem2).Should().BeTrue();
        }

        [Fact(DisplayName = "FSE-004: GetFilesAsync calls TraverseAsync with correct options")]
        public async Task FSE004()
        {
            // Arrange
            var mockFileSystem = new Mock<IReadOnlyFileSystem>();
            var expectedSummary = new TraversalSummary(2, 0, 0, System.TimeSpan.Zero, false);
            
            mockFileSystem.Setup(fs => fs.TraverseAsync(
                    It.IsAny<CanonicalPath>(),
                    It.IsAny<ITraversalHandler>(),
                    It.IsAny<TraversalOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns((CanonicalPath path, ITraversalHandler handler, TraversalOptions options, CancellationToken token) =>
                {
                    // Simulate collecting files by accessing the handler's paths
                    if (handler is PathCollectorHandler collector)
                    {
                        // Use reflection to simulate files being collected
                        var pathsField = typeof(PathCollectorHandler).GetField("paths", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var pathsList = (List<CanonicalPath>)pathsField!.GetValue(collector)!;
                        pathsList.Add("/test/file1.txt".ToCanonicalPath());
                        pathsList.Add("/test/file2.txt".ToCanonicalPath());
                    }
                    return Task.FromResult(Result<TraversalSummary, FileSystemError>.Success(expectedSummary));
                });

            // Act
            var result = await mockFileSystem.Object.GetFilesAsync(
                "/test".ToCanonicalPath(), 
                "*.txt", 
                recursive: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().HaveCount(2);
            result.Value.Should().Contain("/test/file1.txt".ToCanonicalPath());
            result.Value.Should().Contain("/test/file2.txt".ToCanonicalPath());
            
            mockFileSystem.Verify(fs => fs.TraverseAsync(
                "/test".ToCanonicalPath(),
                It.Is<ITraversalHandler>(h => h is PathCollectorHandler),
                It.Is<TraversalOptions>(o => 
                    o.Recursive == false &&
                    o.StrategyHint == TraversalStrategyHint.Search),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact(DisplayName = "FSE-005: GetFilesAsync with recursive option sets correct traversal options")]
        public async Task FSE005()
        {
            // Arrange
            var mockFileSystem = new Mock<IReadOnlyFileSystem>();
            var expectedSummary = new TraversalSummary(1, 0, 0, System.TimeSpan.Zero, false);
            
            mockFileSystem.Setup(fs => fs.TraverseAsync(
                    It.IsAny<CanonicalPath>(),
                    It.IsAny<ITraversalHandler>(),
                    It.IsAny<TraversalOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<TraversalSummary, FileSystemError>.Success(expectedSummary));

            // Act
            await mockFileSystem.Object.GetFilesAsync(
                "/test".ToCanonicalPath(), 
                "*", 
                recursive: true);

            // Assert
            mockFileSystem.Verify(fs => fs.TraverseAsync(
                "/test".ToCanonicalPath(),
                It.IsAny<ITraversalHandler>(),
                It.Is<TraversalOptions>(o => o.Recursive == true),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact(DisplayName = "FSE-006: GetDirectoriesAsync calls TraverseAsync with correct options")]
        public async Task FSE006()
        {
            // Arrange
            var mockFileSystem = new Mock<IReadOnlyFileSystem>();
            var expectedSummary = new TraversalSummary(0, 2, 0, System.TimeSpan.Zero, false);
            
            mockFileSystem.Setup(fs => fs.TraverseAsync(
                    It.IsAny<CanonicalPath>(),
                    It.IsAny<ITraversalHandler>(),
                    It.IsAny<TraversalOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns((CanonicalPath path, ITraversalHandler handler, TraversalOptions options, CancellationToken token) =>
                {
                    // Simulate collecting directories
                    if (handler is PathCollectorHandler collector)
                    {
                        var pathsField = typeof(PathCollectorHandler).GetField("paths", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var pathsList = (List<CanonicalPath>)pathsField!.GetValue(collector)!;
                        pathsList.Add("/test/dir1".ToCanonicalPath());
                        pathsList.Add("/test/dir2".ToCanonicalPath());
                    }
                    return Task.FromResult(Result<TraversalSummary, FileSystemError>.Success(expectedSummary));
                });

            // Act
            var result = await mockFileSystem.Object.GetDirectoriesAsync(
                "/test".ToCanonicalPath(), 
                "*", 
                recursive: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().HaveCount(2);
            result.Value.Should().Contain("/test/dir1".ToCanonicalPath());
            result.Value.Should().Contain("/test/dir2".ToCanonicalPath());
            
            mockFileSystem.Verify(fs => fs.TraverseAsync(
                "/test".ToCanonicalPath(),
                It.Is<ITraversalHandler>(h => h is PathCollectorHandler),
                It.Is<TraversalOptions>(o => 
                    o.Recursive == false &&
                    o.StrategyHint == TraversalStrategyHint.Search),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact(DisplayName = "FSE-007: GetFilesAsync propagates traversal errors")]
        public async Task FSE007()
        {
            // Arrange
            var mockFileSystem = new Mock<IReadOnlyFileSystem>();
            var expectedError = new FileSystemError("Test error", "/test", FileSystemErrorType.AccessDenied);
            
            mockFileSystem.Setup(fs => fs.TraverseAsync(
                    It.IsAny<CanonicalPath>(),
                    It.IsAny<ITraversalHandler>(),
                    It.IsAny<TraversalOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<TraversalSummary, FileSystemError>.Failure(expectedError));

            // Act
            var result = await mockFileSystem.Object.GetFilesAsync("/test".ToCanonicalPath());

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Should().Be(expectedError);
        }

        [Fact(DisplayName = "FSE-008: GetDirectoriesAsync propagates traversal errors")]
        public async Task FSE008()
        {
            // Arrange
            var mockFileSystem = new Mock<IReadOnlyFileSystem>();
            var expectedError = new FileSystemError("Test error", "/test", FileSystemErrorType.AccessDenied);
            
            mockFileSystem.Setup(fs => fs.TraverseAsync(
                    It.IsAny<CanonicalPath>(),
                    It.IsAny<ITraversalHandler>(),
                    It.IsAny<TraversalOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<TraversalSummary, FileSystemError>.Failure(expectedError));

            // Act
            var result = await mockFileSystem.Object.GetDirectoriesAsync("/test".ToCanonicalPath());

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Should().Be(expectedError);
        }
    }
}
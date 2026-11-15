using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Mapping;
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
            var expectedResult = Result<bool, Error>.Success(true);
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

        [Fact(DisplayName = "FSE-004: GetFilesAsync returns matching files")]
        public async Task FSE004()
        {
            // Arrange - use real InMemoryFileSystem instead of mocking with reflection
            var fileSystem = new Ave.Extensions.FileSystem.InMemory.InMemoryFileSystem();
            await fileSystem.CreateDirectoryAsync("/test".ToCanonicalPath());
            await fileSystem.WriteAllTextAsync("/test/file1.txt".ToCanonicalPath(), "content1");
            await fileSystem.WriteAllTextAsync("/test/file2.txt".ToCanonicalPath(), "content2");
            await fileSystem.WriteAllTextAsync("/test/other.doc".ToCanonicalPath(), "doc content");

            // Act
            var result = await fileSystem.GetFilesAsync(
                "/test".ToCanonicalPath(),
                "*.txt",
                recursive: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().HaveCount(2);
            result.Value.Should().Contain("/test/file1.txt".ToCanonicalPath());
            result.Value.Should().Contain("/test/file2.txt".ToCanonicalPath());
            result.Value.Should().NotContain("/test/other.doc".ToCanonicalPath());
        }

        [Fact(DisplayName = "FSE-005: GetFilesAsync with recursive option sets correct traversal options")]
        public async Task FSE005()
        {
            // Arrange
            var mockFileSystem = new Mock<IReadOnlyFileSystem>();
            var expectedSummary = new TraversalSummary(1, 0, 0, System.TimeSpan.Zero, false);
            
            mockFileSystem.Setup(fs => fs.TraverseAsync(
                    It.IsAny<CanonicalPath>(),
                    It.IsAny<ITraversalHandler<FileSystemEntry>>(),
                    It.IsAny<IFileSystemEntryMapper<FileSystemEntry>>(),
                    It.IsAny<TraversalOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<TraversalSummary, Error>.Success(expectedSummary));

            // Act
            await mockFileSystem.Object.GetFilesAsync(
                "/test".ToCanonicalPath(), 
                "*", 
                recursive: true);

            // Assert
            mockFileSystem.Verify(fs => fs.TraverseAsync(
                "/test".ToCanonicalPath(),
                It.IsAny<ITraversalHandler<FileSystemEntry>>(),
                It.IsAny<IFileSystemEntryMapper<FileSystemEntry>>(),
                It.Is<TraversalOptions>(o => o.Recursive == true),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact(DisplayName = "FSE-006: GetDirectoriesAsync returns matching directories")]
        public async Task FSE006()
        {
            // Arrange - use real InMemoryFileSystem instead of mocking with reflection
            var fileSystem = new Ave.Extensions.FileSystem.InMemory.InMemoryFileSystem();
            await fileSystem.CreateDirectoryAsync("/test".ToCanonicalPath());
            await fileSystem.CreateDirectoryAsync("/test/dir1".ToCanonicalPath());
            await fileSystem.CreateDirectoryAsync("/test/dir2".ToCanonicalPath());
            await fileSystem.CreateDirectoryAsync("/test/other".ToCanonicalPath());

            // Act
            var result = await fileSystem.GetDirectoriesAsync(
                "/test".ToCanonicalPath(),
                "dir*",
                recursive: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().HaveCount(2);
            result.Value.Should().Contain("/test/dir1".ToCanonicalPath());
            result.Value.Should().Contain("/test/dir2".ToCanonicalPath());
            result.Value.Should().NotContain("/test/other".ToCanonicalPath());
        }

        [Fact(DisplayName = "FSE-007: GetFilesAsync propagates traversal errors")]
        public async Task FSE007()
        {
            // Arrange
            var mockFileSystem = new Mock<IReadOnlyFileSystem>();
            var expectedError = new Error(FileSystemErrorCodes.IO.AccessDenied, "Test error").With("path", "/test");
            
            mockFileSystem.Setup(fs => fs.TraverseAsync(
                    It.IsAny<CanonicalPath>(),
                    It.IsAny<ITraversalHandler<FileSystemEntry>>(),
                    It.IsAny<IFileSystemEntryMapper<FileSystemEntry>>(),
                    It.IsAny<TraversalOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<TraversalSummary, Error>.Failure(expectedError));

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
            var expectedError = new Error(FileSystemErrorCodes.IO.AccessDenied, "Test error").With("path", "/test");
            
            mockFileSystem.Setup(fs => fs.TraverseAsync(
                    It.IsAny<CanonicalPath>(),
                    It.IsAny<ITraversalHandler<FileSystemEntry>>(),
                    It.IsAny<IFileSystemEntryMapper<FileSystemEntry>>(),
                    It.IsAny<TraversalOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<TraversalSummary, Error>.Failure(expectedError));

            // Act
            var result = await mockFileSystem.Object.GetDirectoriesAsync("/test".ToCanonicalPath());

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Should().Be(expectedError);
        }
    }
}
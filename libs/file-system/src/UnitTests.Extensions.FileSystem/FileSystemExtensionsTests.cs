using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Models;
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
    }
}
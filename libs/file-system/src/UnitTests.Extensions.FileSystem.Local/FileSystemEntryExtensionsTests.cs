using System;
using System.IO;
using System.Runtime.InteropServices;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.Functional;
using FluentAssertions;
using Moq;
using UnitTests.Extensions.FileSystem.Local.TestHelpers;

namespace UnitTests.Extensions.FileSystem.Local
{
    /// <summary>
    /// Unit tests for FileSystemEntryExtensions
    /// </summary>
    public class FileSystemEntryExtensionsTests
    {
        [Fact(DisplayName = "FSEE-001: ToFileSystemEntry converts FileInfo correctly")]
        public void FSEE001()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            var expectedContent = "test content";
            File.WriteAllText(tempFile, expectedContent);
            
            try
            {
                var fileInfo = new FileInfo(tempFile);
                var expectedCanonicalPath = "/test/file.txt".ToCanonicalPath();
                
                var mockPathMapper = new Mock<IPathMapper>();
                mockPathMapper.Setup(pm => pm.MapToCanonicalPath(fileInfo.FullName))
                    .Returns(Result<CanonicalPath, Error>.Success(expectedCanonicalPath));
                
                // Act
                var entry = fileInfo.ToFileSystemEntry(mockPathMapper.Object);
                
                // Assert
                entry.Should().NotBeNull();
                entry.Path.Should().Be(expectedCanonicalPath);
                entry.IsDirectory.Should().BeFalse();
                entry.Size.Should().Be(expectedContent.Length);
                entry.Extension.Should().Be(Path.GetExtension(tempFile));
                entry.Attributes.Should().Be(fileInfo.Attributes);
                mockPathMapper.Verify(pm => pm.MapToCanonicalPath(fileInfo.FullName), Times.Once);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact(DisplayName = "FSEE-002: ToFileSystemEntry converts DirectoryInfo correctly")]
        public void FSEE002()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            
            try
            {
                var directoryInfo = new DirectoryInfo(tempDir);
                var expectedCanonicalPath = "/test/directory".ToCanonicalPath();
                
                var mockPathMapper = new Mock<IPathMapper>();
                mockPathMapper.Setup(pm => pm.MapToCanonicalPath(directoryInfo.FullName))
                    .Returns(Result<CanonicalPath, Error>.Success(expectedCanonicalPath));
                
                // Act
                var entry = directoryInfo.ToFileSystemEntry(mockPathMapper.Object);
                
                // Assert
                entry.Should().NotBeNull();
                entry.Path.Should().Be(expectedCanonicalPath);
                entry.IsDirectory.Should().BeTrue();
                entry.Size.Should().BeNull();
                entry.Extension.Should().BeNull();
                entry.Attributes.Should().HaveFlag(FileAttributes.Directory);
                mockPathMapper.Verify(pm => pm.MapToCanonicalPath(directoryInfo.FullName), Times.Once);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact(DisplayName = "FSEE-003: ToFileSystemEntry throws ArgumentNullException for null FileInfo")]
        public void FSEE003()
        {
            // Arrange
            FileInfo fileInfo = null!;
            var mockPathMapper = new Mock<IPathMapper>();
            
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => fileInfo.ToFileSystemEntry(mockPathMapper.Object));
        }

        [Fact(DisplayName = "FSEE-004: ToFileSystemEntry throws ArgumentNullException for null DirectoryInfo")]
        public void FSEE004()
        {
            // Arrange
            DirectoryInfo directoryInfo = null!;
            var mockPathMapper = new Mock<IPathMapper>();
            
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => directoryInfo.ToFileSystemEntry(mockPathMapper.Object));
        }

        [Fact(DisplayName = "FSEE-013: ToFileSystemEntry throws ArgumentNullException for null PathMapper with FileInfo")]
        public void FSEE013()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            try
            {
                var fileInfo = new FileInfo(tempFile);
                IPathMapper pathMapper = null!;
                
                // Act & Assert
                Assert.Throws<ArgumentNullException>(() => fileInfo.ToFileSystemEntry(pathMapper));
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact(DisplayName = "FSEE-014: ToFileSystemEntry throws ArgumentNullException for null PathMapper with DirectoryInfo")]
        public void FSEE014()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                var directoryInfo = new DirectoryInfo(tempDir);
                IPathMapper pathMapper = null!;
                
                // Act & Assert
                Assert.Throws<ArgumentNullException>(() => directoryInfo.ToFileSystemEntry(pathMapper));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact(DisplayName = "FSEE-005: ToFileSystemEntry uses path mapper when provided for FileInfo")]
        public void FSEE005()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            var expectedContent = "test content";
            File.WriteAllText(tempFile, expectedContent);
            
            try
            {
                var fileInfo = new FileInfo(tempFile);
                var expectedCanonicalPath = "/mapped/test/file.txt".ToCanonicalPath();
                
                var mockPathMapper = new Mock<IPathMapper>();
                mockPathMapper.Setup(pm => pm.MapToCanonicalPath(fileInfo.FullName))
                    .Returns(Result<CanonicalPath, Error>.Success(expectedCanonicalPath));
                
                // Act
                var entry = fileInfo.ToFileSystemEntry(mockPathMapper.Object);
                
                // Assert
                entry.Should().NotBeNull();
                entry.Path.Should().Be(expectedCanonicalPath);
                entry.IsDirectory.Should().BeFalse();
                entry.Size.Should().Be(expectedContent.Length);
                mockPathMapper.Verify(pm => pm.MapToCanonicalPath(fileInfo.FullName), Times.Once);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact(DisplayName = "FSEE-006: ToFileSystemEntry uses path mapper when provided for DirectoryInfo")]
        public void FSEE006()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            
            try
            {
                var directoryInfo = new DirectoryInfo(tempDir);
                var expectedCanonicalPath = "/mapped/test/directory".ToCanonicalPath();
                
                var mockPathMapper = new Mock<IPathMapper>();
                mockPathMapper.Setup(pm => pm.MapToCanonicalPath(directoryInfo.FullName))
                    .Returns(Result<CanonicalPath, Error>.Success(expectedCanonicalPath));
                
                // Act
                var entry = directoryInfo.ToFileSystemEntry(mockPathMapper.Object);
                
                // Assert
                entry.Should().NotBeNull();
                entry.Path.Should().Be(expectedCanonicalPath);
                entry.IsDirectory.Should().BeTrue();
                entry.Size.Should().BeNull();
                entry.Extension.Should().BeNull();
                mockPathMapper.Verify(pm => pm.MapToCanonicalPath(directoryInfo.FullName), Times.Once);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact(DisplayName = "FSEE-007: ToFileSystemEntry throws when path mapper fails for FileInfo")]
        public void FSEE007()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, "test");
            
            try
            {
                var fileInfo = new FileInfo(tempFile);
                var expectedError = new Error("INVALID_PATH", "Invalid path provided");
                
                var mockPathMapper = new Mock<IPathMapper>();
                mockPathMapper.Setup(pm => pm.MapToCanonicalPath(fileInfo.FullName))
                    .Returns(Result<CanonicalPath, Error>.Failure(expectedError));
                
                // Act & Assert
                var exception = Assert.Throws<ArgumentException>(() => fileInfo.ToFileSystemEntry(mockPathMapper.Object));
                exception.Message.Should().Contain($"Invalid file path: {fileInfo.FullName}");
                exception.ParamName.Should().Be("fileInfo");
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact(DisplayName = "FSEE-008: ToFileSystemEntry throws when path mapper fails for DirectoryInfo")]
        public void FSEE008()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            
            try
            {
                var directoryInfo = new DirectoryInfo(tempDir);
                var expectedError = new Error("INVALID_PATH", "Invalid path provided");
                
                var mockPathMapper = new Mock<IPathMapper>();
                mockPathMapper.Setup(pm => pm.MapToCanonicalPath(directoryInfo.FullName))
                    .Returns(Result<CanonicalPath, Error>.Failure(expectedError));
                
                // Act & Assert
                var exception = Assert.Throws<ArgumentException>(() => directoryInfo.ToFileSystemEntry(mockPathMapper.Object));
                exception.Message.Should().Contain($"Invalid directory path: {directoryInfo.FullName}");
                exception.ParamName.Should().Be("directoryInfo");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact(DisplayName = "FSEE-009: ToFileSystemEntry handles platform-specific paths correctly with path mapper")]
        public void FSEE009()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            var expectedContent = "test content";
            File.WriteAllText(tempFile, expectedContent);
            
            try
            {
                var fileInfo = new FileInfo(tempFile);
                var expectedCanonicalPath = "/platform/specific/file.txt".ToCanonicalPath();
                
                var mockPathMapper = new Mock<IPathMapper>();
                mockPathMapper.Setup(pm => pm.MapToCanonicalPath(fileInfo.FullName))
                    .Returns(Result<CanonicalPath, Error>.Success(expectedCanonicalPath));
                
                // Act
                var entry = fileInfo.ToFileSystemEntry(mockPathMapper.Object);
                
                // Assert
                entry.Should().NotBeNull();
                entry.Path.Should().Be(expectedCanonicalPath);
                
                // Canonical paths should always use forward slashes
                entry.Path.Value.Should().NotContain("\\", "canonical paths should use forward slashes on all platforms");
                entry.Path.Value.Should().Contain("/", "canonical paths should use forward slashes");
                entry.Path.Value.Should().StartWith("/", "canonical paths must start with forward slash");
                
                entry.IsDirectory.Should().BeFalse();
                entry.Size.Should().Be(expectedContent.Length);
                mockPathMapper.Verify(pm => pm.MapToCanonicalPath(fileInfo.FullName), Times.Once);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact(DisplayName = "FSEE-010: ToFileSystemEntry preserves file timestamps correctly")]
        public void FSEE010()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, "test content");
            
            try
            {
                var fileInfo = new FileInfo(tempFile);
                var expectedCanonicalPath = "/test/timestamps/file.txt".ToCanonicalPath();
                
                var mockPathMapper = new Mock<IPathMapper>();
                mockPathMapper.Setup(pm => pm.MapToCanonicalPath(fileInfo.FullName))
                    .Returns(Result<CanonicalPath, Error>.Success(expectedCanonicalPath));
                
                // Refresh to get current timestamps from filesystem
                fileInfo.Refresh();
                var originalCreatedUtc = fileInfo.CreationTimeUtc;
                var originalModifiedUtc = fileInfo.LastWriteTimeUtc;
                var originalAccessedUtc = fileInfo.LastAccessTimeUtc;
                
                // Act
                var entry = fileInfo.ToFileSystemEntry(mockPathMapper.Object);
                
                // Assert - verify the entry contains the actual timestamps from the file system
                entry.CreatedUtc.Should().Be(originalCreatedUtc);
                entry.ModifiedUtc.Should().Be(originalModifiedUtc);
                entry.LastAccessUtc.Should().Be(originalAccessedUtc);
                mockPathMapper.Verify(pm => pm.MapToCanonicalPath(fileInfo.FullName), Times.Once);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact(DisplayName = "FSEE-011: ToFileSystemEntry preserves directory timestamps correctly")]
        public void FSEE011()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            
            try
            {
                var directoryInfo = new DirectoryInfo(tempDir);
                var expectedCanonicalPath = "/test/timestamps/directory".ToCanonicalPath();
                
                var mockPathMapper = new Mock<IPathMapper>();
                mockPathMapper.Setup(pm => pm.MapToCanonicalPath(directoryInfo.FullName))
                    .Returns(Result<CanonicalPath, Error>.Success(expectedCanonicalPath));
                
                // Refresh to get current timestamps from filesystem
                directoryInfo.Refresh();
                var originalCreatedUtc = directoryInfo.CreationTimeUtc;
                var originalModifiedUtc = directoryInfo.LastWriteTimeUtc;
                var originalAccessedUtc = directoryInfo.LastAccessTimeUtc;
                
                // Act
                var entry = directoryInfo.ToFileSystemEntry(mockPathMapper.Object);
                
                // Assert - verify the entry contains the actual timestamps from the file system
                entry.CreatedUtc.Should().Be(originalCreatedUtc);
                entry.ModifiedUtc.Should().Be(originalModifiedUtc);
                entry.LastAccessUtc.Should().Be(originalAccessedUtc);
                mockPathMapper.Verify(pm => pm.MapToCanonicalPath(directoryInfo.FullName), Times.Once);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact(DisplayName = "FSEE-012: ToFileSystemEntry handles file attributes correctly")]
        public void FSEE012()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, "test content");
            
            try
            {
                var fileInfo = new FileInfo(tempFile);
                var expectedCanonicalPath = "/test/attributes/file.txt".ToCanonicalPath();
                
                var mockPathMapper = new Mock<IPathMapper>();
                mockPathMapper.Setup(pm => pm.MapToCanonicalPath(fileInfo.FullName))
                    .Returns(Result<CanonicalPath, Error>.Success(expectedCanonicalPath));
                
                // Set read-only attribute on supported platforms
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || 
                    RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    fileInfo.Attributes |= FileAttributes.ReadOnly;
                }
                
                // Act
                var entry = fileInfo.ToFileSystemEntry(mockPathMapper.Object);
                
                // Assert
                entry.Attributes.Should().Be(fileInfo.Attributes);
                entry.IsReadOnly.Should().Be(fileInfo.Attributes.HasFlag(FileAttributes.ReadOnly));
                mockPathMapper.Verify(pm => pm.MapToCanonicalPath(fileInfo.FullName), Times.Once);
            }
            finally
            {
                // Remove read-only before deletion
                if (File.Exists(tempFile))
                {
                    File.SetAttributes(tempFile, FileAttributes.Normal);
                    File.Delete(tempFile);
                }
            }
        }
    }
}
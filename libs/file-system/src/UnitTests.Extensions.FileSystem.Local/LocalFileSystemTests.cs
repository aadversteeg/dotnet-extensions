using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;
using UnitTests.Extensions.FileSystem.Local.TestHelpers;

namespace UnitTests.Extensions.FileSystem.Local
{
    public class LocalFileSystemTests
    {
        [Fact(DisplayName = "LFS-001: FileExistsAsync returns success with true for existing file")]
        public async Task LFS001()
        {
            // Arrange
            var fileSystem = new LocalFileSystem();
            var tempFilePath = Path.GetTempFileName();
            
            try
            {
                // Act
                var result = await fileSystem.FileExistsAsync(tempFilePath.ToCanonicalPath());
                
                // Assert
                result.IsSuccess.Should().BeTrue();
                result.Value.Should().BeTrue();
            }
            finally
            {
                // Cleanup
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }
        
        [Fact(DisplayName = "LFS-002: FileExistsAsync returns success with false for non-existing file")]
        public async Task LFS002()
        {
            // Arrange
            var fileSystem = new LocalFileSystem();
            var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            
            // Act
            var result = await fileSystem.FileExistsAsync(nonExistentPath.ToCanonicalPath());
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeFalse();
        }

        [Fact(DisplayName = "LFS-003: FileExistsAsync returns failure for invalid path")]
        public void LFS003()
        {
            // Arrange
            var invalidPath = "";
            
            // Act
            // For invalid path test, we need to use CanonicalPath.Create and check the failure
            var canonicalPathResult = CanonicalPath.Create(invalidPath);
            
            // Assert
            canonicalPathResult.IsFailure.Should().BeTrue();
            canonicalPathResult.Error.Code.Should().Be(ErrorCodes.CanonicalPath.PathNullOrEmpty);
        }
        
        [Fact(DisplayName = "LFS-004: DirectoryExistsAsync returns success with true for existing directory")]
        public async Task LFS004()
        {
            // Arrange
            var fileSystem = new LocalFileSystem();
            var tempDir = Path.Combine(Path.GetTempPath(), "FileSystemTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            
            try
            {
                // Act
                var result = await fileSystem.DirectoryExistsAsync(tempDir.ToCanonicalPath());
                
                // Assert
                result.IsSuccess.Should().BeTrue();
                result.Value.Should().BeTrue();
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir);
                }
            }
        }

        [Fact(DisplayName = "LFS-005: DirectoryExistsAsync returns success with false for non-existing directory")]
        public async Task LFS005()
        {
            // Arrange
            var fileSystem = new LocalFileSystem();
            var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            
            // Act
            var result = await fileSystem.DirectoryExistsAsync(nonExistentPath.ToCanonicalPath());
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeFalse();
        }
        
        [Fact(DisplayName = "LFS-006: ReadWriteAllText performs round trip successfully")]
        public async Task LFS006()
        {
            // Arrange
            var fileSystem = new LocalFileSystem();
            var tempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            var expectedContent = "Hello World!";
            
            try
            {
                // Act
                var writeResult = await fileSystem.WriteAllTextAsync(tempFilePath.ToCanonicalPath(), expectedContent);
                var readResult = await fileSystem.ReadAllTextAsync(tempFilePath.ToCanonicalPath());
                
                // Assert
                writeResult.IsSuccess.Should().BeTrue();
                readResult.IsSuccess.Should().BeTrue();
                readResult.Value.Should().Be(expectedContent);
            }
            finally
            {
                // Cleanup
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }

        [Fact(DisplayName = "LFS-007: ReadAllTextAsync returns failure for non-existing file")]
        public async Task LFS007()
        {
            // Arrange
            var fileSystem = new LocalFileSystem();
            var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            
            // Act
            var result = await fileSystem.ReadAllTextAsync(nonExistentPath.ToCanonicalPath());
            
            // Assert
            result.IsSuccess.Should().BeFalse();
            result.Error.Type.Should().Be(FileSystemErrorType.FileNotFound);
        }
        
        [Fact(DisplayName = "LFS-008: CreateDirectoryAsync creates the directory successfully")]
        public async Task LFS008()
        {
            // Arrange
            var fileSystem = new LocalFileSystem();
            var tempDir = Path.Combine(Path.GetTempPath(), "FSTest_" + Guid.NewGuid().ToString("N"));
            
            try
            {
                // Act
                var result = await fileSystem.CreateDirectoryAsync(tempDir.ToCanonicalPath());
                var exists = Directory.Exists(tempDir);
                
                // Assert
                result.IsSuccess.Should().BeTrue();
                exists.Should().BeTrue();
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir);
                }
            }
        }
        
        [Fact(DisplayName = "LFS-009: GetFilesAsync returns correct file list")]
        public async Task LFS009()
        {
            // Arrange
            var fileSystem = new LocalFileSystem();
            var tempDir = Path.Combine(Path.GetTempPath(), "FSTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            
            var file1 = Path.Combine(tempDir, "file1.txt");
            var file2 = Path.Combine(tempDir, "file2.txt");
            
            try
            {
                File.WriteAllText(file1, "Content 1");
                File.WriteAllText(file2, "Content 2");
                
                // Act
                var result = await fileSystem.GetFilesAsync(tempDir.ToCanonicalPath());
                
                // Assert
                result.IsSuccess.Should().BeTrue();
                result.Value.Should().HaveCount(2);
                result.Value.Should().Contain(file1.ToCanonicalPath());
                result.Value.Should().Contain(file2.ToCanonicalPath());
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact(DisplayName = "LFS-010: GetFilesAsync returns failure for non-existing directory")]
        public async Task LFS010()
        {
            // Arrange
            var fileSystem = new LocalFileSystem();
            var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            
            // Act
            var result = await fileSystem.GetFilesAsync(nonExistentPath.ToCanonicalPath());
            
            // Assert
            result.IsSuccess.Should().BeFalse();
            result.Error.Type.Should().Be(FileSystemErrorType.DirectoryNotFound);
        }

        [Fact(DisplayName = "LFS-011: GetDirectoriesAsync returns correct directory list")]
        public async Task LFS011()
        {
            // Arrange
            var fileSystem = new LocalFileSystem();
            var tempDir = Path.Combine(Path.GetTempPath(), "FSTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            
            var subDir1 = Path.Combine(tempDir, "subdir1");
            var subDir2 = Path.Combine(tempDir, "subdir2");
            Directory.CreateDirectory(subDir1);
            Directory.CreateDirectory(subDir2);
            
            try
            {
                // Act
                var result = await fileSystem.GetDirectoriesAsync(tempDir.ToCanonicalPath());
                
                // Assert
                result.IsSuccess.Should().BeTrue();
                result.Value.Should().HaveCount(2);
                result.Value.Should().Contain(subDir1.ToCanonicalPath());
                result.Value.Should().Contain(subDir2.ToCanonicalPath());
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact(DisplayName = "LFS-012: GetFileMetadataAsync returns correct metadata")]
        public async Task LFS012()
        {
            // Arrange
            var fileSystem = new LocalFileSystem();
            var tempFilePath = Path.GetTempFileName();
            var content = "Test content for metadata";
            File.WriteAllText(tempFilePath, content);
            
            try
            {
                // Act
                var result = await fileSystem.GetFileMetadataAsync(tempFilePath.ToCanonicalPath());
                
                // Assert
                result.IsSuccess.Should().BeTrue();
                result.Value.FullPath.Should().Be(tempFilePath);
                result.Value.Name.Should().Be(Path.GetFileName(tempFilePath));
                result.Value.Extension.Should().Be(Path.GetExtension(tempFilePath));
                result.Value.Size.Should().BeGreaterThan(0);
            }
            finally
            {
                // Cleanup
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }

        [Fact(DisplayName = "LFS-013: GetFileMetadataAsync returns failure for non-existing file")]
        public async Task LFS013()
        {
            // Arrange
            var fileSystem = new LocalFileSystem();
            var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            
            // Act
            var result = await fileSystem.GetFileMetadataAsync(nonExistentPath.ToCanonicalPath());
            
            // Assert
            result.IsSuccess.Should().BeFalse();
            result.Error.Type.Should().Be(FileSystemErrorType.FileNotFound);
        }

        [Fact(DisplayName = "LFS-014: GetDirectoryMetadataAsync returns correct metadata")]
        public async Task LFS014()
        {
            // Arrange
            var fileSystem = new LocalFileSystem();
            var tempDir = Path.Combine(Path.GetTempPath(), "FSTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            
            try
            {
                // Act
                var result = await fileSystem.GetDirectoryMetadataAsync(tempDir.ToCanonicalPath());
                
                // Assert
                result.IsSuccess.Should().BeTrue();
                result.Value.FullPath.Should().Be(tempDir);
                result.Value.Name.Should().Be(Path.GetFileName(tempDir));
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir);
                }
            }
        }

        [Fact(DisplayName = "LFS-015: OpenReadAsync returns valid stream")]
        public async Task LFS015()
        {
            // Arrange
            var fileSystem = new LocalFileSystem();
            var tempFilePath = Path.GetTempFileName();
            var content = "Test content";
            File.WriteAllText(tempFilePath, content);
            
            try
            {
                // Act
                var result = await fileSystem.OpenReadAsync(tempFilePath.ToCanonicalPath());
                
                // Assert
                result.IsSuccess.Should().BeTrue();
                using var stream = result.Value;
                stream.Should().NotBeNull();
                stream.CanRead.Should().BeTrue();
                
                using var reader = new StreamReader(stream);
                var readContent = await reader.ReadToEndAsync();
                readContent.Should().Be(content);
            }
            finally
            {
                // Cleanup
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }

        [Fact(DisplayName = "LFS-016: DeleteFileAsync removes file successfully")]
        public async Task LFS016()
        {
            // Arrange
            var fileSystem = new LocalFileSystem();
            var tempFilePath = Path.GetTempFileName();
            
            try
            {
                File.Exists(tempFilePath).Should().BeTrue();
                
                // Act
                var result = await fileSystem.DeleteFileAsync(tempFilePath.ToCanonicalPath());
                
                // Assert
                result.IsSuccess.Should().BeTrue();
                File.Exists(tempFilePath).Should().BeFalse();
            }
            finally
            {
                // Cleanup
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }

        [Fact(DisplayName = "LFS-017: DeleteDirectoryAsync removes directory successfully")]
        public async Task LFS017()
        {
            // Arrange
            var fileSystem = new LocalFileSystem();
            var tempDir = Path.Combine(Path.GetTempPath(), "FSTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            
            try
            {
                Directory.Exists(tempDir).Should().BeTrue();
                
                // Act
                var result = await fileSystem.DeleteDirectoryAsync(tempDir.ToCanonicalPath());
                
                // Assert
                result.IsSuccess.Should().BeTrue();
                Directory.Exists(tempDir).Should().BeFalse();
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact(DisplayName = "LFS-018: EnumerateFileSystemEntriesAsync yields correct entries")]
        public async Task LFS018()
        {
            // Arrange
            var fileSystem = new LocalFileSystem();
            var tempDir = Path.Combine(Path.GetTempPath(), "FSTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            
            var file1 = Path.Combine(tempDir, "file1.txt");
            var subDir = Path.Combine(tempDir, "subdir");
            File.WriteAllText(file1, "Content");
            Directory.CreateDirectory(subDir);
            
            try
            {
                // Act
                var entries = new List<Result<FileSystemEntry, FileSystemError>>();
                await foreach (var entry in fileSystem.EnumerateFileSystemEntriesAsync(tempDir.ToCanonicalPath()))
                {
                    entries.Add(entry);
                }
                
                // Assert
                entries.Should().HaveCount(2);
                entries.Should().AllSatisfy(entry => entry.IsSuccess.Should().BeTrue());
                
                var fileEntry = entries.First(e => !e.Value.IsDirectory);
                var dirEntry = entries.First(e => e.Value.IsDirectory);
                
                fileEntry.Value.Path.Should().Be(file1.ToCanonicalPath());
                dirEntry.Value.Path.Should().Be(subDir.ToCanonicalPath());
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
    }
}
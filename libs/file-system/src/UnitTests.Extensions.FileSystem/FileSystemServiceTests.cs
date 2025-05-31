using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.FileSystem
{
    public class FileSystemServiceTests
    {
        [Fact(DisplayName = "FSS-001: FileExistsAsync returns success with true for existing file")]
        public async Task FSS001()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var tempFilePath = Path.GetTempFileName();
            
            try
            {
                // Act
                var result = await fileSystem.FileExistsAsync(tempFilePath);
                
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
        
        [Fact(DisplayName = "FSS-002: FileExistsAsync returns success with false for non-existing file")]
        public async Task FSS002()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            
            // Act
            var result = await fileSystem.FileExistsAsync(nonExistentPath);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeFalse();
        }

        [Fact(DisplayName = "FSS-003: FileExistsAsync returns failure for invalid path")]
        public async Task FSS003()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var invalidPath = "";
            
            // Act
            var result = await fileSystem.FileExistsAsync(invalidPath);
            
            // Assert
            result.IsSuccess.Should().BeFalse();
            result.Error.Type.Should().Be(FileSystemErrorType.InvalidPath);
        }
        
        [Fact(DisplayName = "FSS-004: DirectoryExistsAsync returns success with true for existing directory")]
        public async Task FSS004()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var tempDir = Path.Combine(Path.GetTempPath(), "FileSystemTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            
            try
            {
                // Act
                var result = await fileSystem.DirectoryExistsAsync(tempDir);
                
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

        [Fact(DisplayName = "FSS-005: DirectoryExistsAsync returns success with false for non-existing directory")]
        public async Task FSS005()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            
            // Act
            var result = await fileSystem.DirectoryExistsAsync(nonExistentPath);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeFalse();
        }
        
        [Fact(DisplayName = "FSS-006: ReadWriteAllText performs round trip successfully")]
        public async Task FSS006()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var tempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            var expectedContent = "Hello World!";
            
            try
            {
                // Act
                var writeResult = await fileSystem.WriteAllTextAsync(tempFilePath, expectedContent);
                var readResult = await fileSystem.ReadAllTextAsync(tempFilePath);
                
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

        [Fact(DisplayName = "FSS-007: ReadAllTextAsync returns failure for non-existing file")]
        public async Task FSS007()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            
            // Act
            var result = await fileSystem.ReadAllTextAsync(nonExistentPath);
            
            // Assert
            result.IsSuccess.Should().BeFalse();
            result.Error.Type.Should().Be(FileSystemErrorType.FileNotFound);
        }
        
        [Fact(DisplayName = "FSS-008: CreateDirectoryAsync creates the directory successfully")]
        public async Task FSS008()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var tempDir = Path.Combine(Path.GetTempPath(), "FSTest_" + Guid.NewGuid().ToString("N"));
            
            try
            {
                // Act
                var result = await fileSystem.CreateDirectoryAsync(tempDir);
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
        
        [Fact(DisplayName = "FSS-009: GetFilesAsync returns correct file list")]
        public async Task FSS009()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var tempDir = Path.Combine(Path.GetTempPath(), "FSTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            
            var file1 = Path.Combine(tempDir, "file1.txt");
            var file2 = Path.Combine(tempDir, "file2.txt");
            
            try
            {
                File.WriteAllText(file1, "Content 1");
                File.WriteAllText(file2, "Content 2");
                
                // Act
                var result = await fileSystem.GetFilesAsync(tempDir);
                
                // Assert
                result.IsSuccess.Should().BeTrue();
                result.Value.Should().HaveCount(2);
                result.Value.Should().Contain(file1);
                result.Value.Should().Contain(file2);
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

        [Fact(DisplayName = "FSS-010: GetFilesAsync returns failure for non-existing directory")]
        public async Task FSS010()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            
            // Act
            var result = await fileSystem.GetFilesAsync(nonExistentPath);
            
            // Assert
            result.IsSuccess.Should().BeFalse();
            result.Error.Type.Should().Be(FileSystemErrorType.DirectoryNotFound);
        }

        [Fact(DisplayName = "FSS-011: GetDirectoriesAsync returns correct directory list")]
        public async Task FSS011()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var tempDir = Path.Combine(Path.GetTempPath(), "FSTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            
            var subDir1 = Path.Combine(tempDir, "subdir1");
            var subDir2 = Path.Combine(tempDir, "subdir2");
            Directory.CreateDirectory(subDir1);
            Directory.CreateDirectory(subDir2);
            
            try
            {
                // Act
                var result = await fileSystem.GetDirectoriesAsync(tempDir);
                
                // Assert
                result.IsSuccess.Should().BeTrue();
                result.Value.Should().HaveCount(2);
                result.Value.Should().Contain(subDir1);
                result.Value.Should().Contain(subDir2);
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

        [Fact(DisplayName = "FSS-012: GetFileMetadataAsync returns correct metadata")]
        public async Task FSS012()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var tempFilePath = Path.GetTempFileName();
            var content = "Test content for metadata";
            File.WriteAllText(tempFilePath, content);
            
            try
            {
                // Act
                var result = await fileSystem.GetFileMetadataAsync(tempFilePath);
                
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

        [Fact(DisplayName = "FSS-013: GetFileMetadataAsync returns failure for non-existing file")]
        public async Task FSS013()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            
            // Act
            var result = await fileSystem.GetFileMetadataAsync(nonExistentPath);
            
            // Assert
            result.IsSuccess.Should().BeFalse();
            result.Error.Type.Should().Be(FileSystemErrorType.FileNotFound);
        }

        [Fact(DisplayName = "FSS-014: GetDirectoryMetadataAsync returns correct metadata")]
        public async Task FSS014()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var tempDir = Path.Combine(Path.GetTempPath(), "FSTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            
            try
            {
                // Act
                var result = await fileSystem.GetDirectoryMetadataAsync(tempDir);
                
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

        [Fact(DisplayName = "FSS-015: OpenReadAsync returns valid stream")]
        public async Task FSS015()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var tempFilePath = Path.GetTempFileName();
            var content = "Test content";
            File.WriteAllText(tempFilePath, content);
            
            try
            {
                // Act
                var result = await fileSystem.OpenReadAsync(tempFilePath);
                
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

        [Fact(DisplayName = "FSS-016: DeleteFileAsync removes file successfully")]
        public async Task FSS016()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var tempFilePath = Path.GetTempFileName();
            
            try
            {
                File.Exists(tempFilePath).Should().BeTrue();
                
                // Act
                var result = await fileSystem.DeleteFileAsync(tempFilePath);
                
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

        [Fact(DisplayName = "FSS-017: DeleteDirectoryAsync removes directory successfully")]
        public async Task FSS017()
        {
            // Arrange
            var fileSystem = new FileSystemService();
            var tempDir = Path.Combine(Path.GetTempPath(), "FSTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            
            try
            {
                Directory.Exists(tempDir).Should().BeTrue();
                
                // Act
                var result = await fileSystem.DeleteDirectoryAsync(tempDir);
                
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

        [Fact(DisplayName = "FSS-018: EnumerateFileSystemEntriesAsync yields correct entries")]
        public async Task FSS018()
        {
            // Arrange
            var fileSystem = new FileSystemService();
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
                await foreach (var entry in fileSystem.EnumerateFileSystemEntriesAsync(tempDir))
                {
                    entries.Add(entry);
                }
                
                // Assert
                entries.Should().HaveCount(2);
                entries.Should().AllSatisfy(entry => entry.IsSuccess.Should().BeTrue());
                
                var fileEntry = entries.First(e => !e.Value.IsDirectory);
                var dirEntry = entries.First(e => e.Value.IsDirectory);
                
                fileEntry.Value.Path.Should().Be(file1);
                dirEntry.Value.Path.Should().Be(subDir);
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
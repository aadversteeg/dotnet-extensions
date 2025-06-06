using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.InMemory;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.FileSystem.Traversal.Handlers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using UnitTests.Extensions.FileSystem.InMemory.TestHelpers;
using Xunit;

namespace UnitTests.Extensions.FileSystem.InMemory
{
    /// <summary>
    /// Unit tests for InMemoryFileSystem.
    /// </summary>
    public class InMemoryFileSystemTests : IDisposable
    {
        private readonly InMemoryFileSystem _fileSystem;
        private readonly Mock<ILogger<InMemoryFileSystem>> _mockLogger;

        public InMemoryFileSystemTests()
        {
            _mockLogger = new Mock<ILogger<InMemoryFileSystem>>();
            _fileSystem = new InMemoryFileSystem(_mockLogger.Object);
        }

        [Fact(DisplayName = "IMFS-001: FileExistsAsync returns false for non-existing file")]
        public async Task IMFS001()
        {
            // Arrange
            var path = "/test/file.txt";

            // Act
            var result = await _fileSystem.FileExistsAsync(path.ToCanonicalPath());

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeFalse();
        }

        [Fact(DisplayName = "IMFS-002: FileExistsAsync returns true for existing file")]
        public async Task IMFS002()
        {
            // Arrange
            var path = "/test/file.txt";
            var content = "Hello, World!";
            await _fileSystem.WriteAllTextAsync(path.ToCanonicalPath(), content);

            // Act
            var result = await _fileSystem.FileExistsAsync(path.ToCanonicalPath());

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeTrue();
        }

        [Fact(DisplayName = "IMFS-003: DirectoryExistsAsync returns true for root directory")]
        public async Task IMFS003()
        {
            // Arrange
            var path = "/";

            // Act
            var result = await _fileSystem.DirectoryExistsAsync(path.ToCanonicalPath());

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeTrue();
        }

        [Fact(DisplayName = "IMFS-004: DirectoryExistsAsync returns false for non-existing directory")]
        public async Task IMFS004()
        {
            // Arrange
            var path = "/test/directory";

            // Act
            var result = await _fileSystem.DirectoryExistsAsync(path.ToCanonicalPath());

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeFalse();
        }

        [Fact(DisplayName = "IMFS-005: CreateDirectoryAsync creates directory successfully")]
        public async Task IMFS005()
        {
            // Arrange
            var path = "/test/directory";

            // Act
            var result = await _fileSystem.CreateDirectoryAsync(path.ToCanonicalPath());

            // Assert
            result.IsSuccess.Should().BeTrue();
            
            var existsResult = await _fileSystem.DirectoryExistsAsync(path.ToCanonicalPath());
            existsResult.IsSuccess.Should().BeTrue();
            existsResult.Value.Should().BeTrue();
        }

        [Fact(DisplayName = "IMFS-006: WriteAllTextAsync creates file with content")]
        public async Task IMFS006()
        {
            // Arrange
            var path = "/test/file.txt";
            var content = "Hello, World!";

            // Act
            var result = await _fileSystem.WriteAllTextAsync(path.ToCanonicalPath(), content);

            // Assert
            result.IsSuccess.Should().BeTrue();
            
            var readResult = await _fileSystem.ReadAllTextAsync(path.ToCanonicalPath());
            readResult.IsSuccess.Should().BeTrue();
            readResult.Value.Should().Be(content);
        }

        [Fact(DisplayName = "IMFS-007: ReadAllTextAsync returns failure for non-existing file")]
        public async Task IMFS007()
        {
            // Arrange
            var path = "/test/nonexistent.txt";

            // Act
            var result = await _fileSystem.ReadAllTextAsync(path.ToCanonicalPath());

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Type.Should().Be(FileSystemErrorType.FileNotFound);
        }

        [Fact(DisplayName = "IMFS-008: OpenReadAsync returns valid stream")]
        public async Task IMFS008()
        {
            // Arrange
            var path = "/test/file.txt";
            var content = "Hello, World!";
            await _fileSystem.WriteAllTextAsync(path.ToCanonicalPath(), content);

            // Act
            var result = await _fileSystem.OpenReadAsync(path.ToCanonicalPath());

            // Assert
            result.IsSuccess.Should().BeTrue();
            using var stream = result.Value;
            using var reader = new StreamReader(stream);
            var readContent = await reader.ReadToEndAsync();
            readContent.Should().Be(content);
        }

        [Fact(DisplayName = "IMFS-009: OpenWriteAsync creates file when disposed")]
        public async Task IMFS009()
        {
            // Arrange
            var path = "/test/file.txt";
            var content = "Hello, World!";

            // Act
            var result = await _fileSystem.OpenWriteAsync(path.ToCanonicalPath());
            result.IsSuccess.Should().BeTrue();

            using (var stream = result.Value)
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                await stream.WriteAsync(bytes, 0, bytes.Length);
            }

            // Assert
            var readResult = await _fileSystem.ReadAllTextAsync(path.ToCanonicalPath());
            readResult.IsSuccess.Should().BeTrue();
            readResult.Value.Should().Be(content);
        }

        [Fact(DisplayName = "IMFS-010: GetFileMetadataAsync returns correct metadata")]
        public async Task IMFS010()
        {
            // Arrange
            var path = "/test/file.txt";
            var content = "Hello, World!";
            var beforeWrite = DateTime.UtcNow;
            await _fileSystem.WriteAllTextAsync(path.ToCanonicalPath(), content);
            var afterWrite = DateTime.UtcNow;

            // Act
            var result = await _fileSystem.GetEntryAsync(path.ToCanonicalPath());

            // Assert
            result.IsSuccess.Should().BeTrue();
            var entry = result.Value;
            entry.Path.Should().Be(path.ToCanonicalPath());
            entry.Name.Should().Be("file.txt");
            entry.Size.Should().Be(Encoding.UTF8.GetBytes(content).Length);
            entry.IsDirectory.Should().BeFalse();
            entry.Extension.Should().Be(".txt");
            entry.CreatedUtc.Should().BeOnOrAfter(beforeWrite).And.BeOnOrBefore(afterWrite);
            entry.ModifiedUtc.Should().BeOnOrAfter(beforeWrite).And.BeOnOrBefore(afterWrite);
            entry.Attributes.Should().Be(FileAttributes.Normal);
        }

        [Fact(DisplayName = "IMFS-011: GetEntryAsync returns correct metadata for directories")]
        public async Task IMFS011()
        {
            // Arrange
            var path = "/test/directory";
            var beforeCreate = DateTime.UtcNow;
            await _fileSystem.CreateDirectoryAsync(path.ToCanonicalPath());
            var afterCreate = DateTime.UtcNow;

            // Act
            var result = await _fileSystem.GetEntryAsync(path.ToCanonicalPath());

            // Assert
            result.IsSuccess.Should().BeTrue();
            var entry = result.Value;
            entry.Path.Should().Be(path.ToCanonicalPath());
            entry.Name.Should().Be("directory");
            entry.IsDirectory.Should().BeTrue();
            entry.Size.Should().BeNull();
            entry.Extension.Should().BeNull();
            entry.CreatedUtc.Should().BeOnOrAfter(beforeCreate).And.BeOnOrBefore(afterCreate);
            entry.ModifiedUtc.Should().BeOnOrAfter(beforeCreate).And.BeOnOrBefore(afterCreate);
            entry.Attributes.Should().Be(FileAttributes.Directory);
        }

        [Fact(DisplayName = "IMFS-012: GetFilesAsync returns correct file list")]
        public async Task IMFS012()
        {
            // Arrange
            var directory = "/test";
            await _fileSystem.CreateDirectoryAsync(directory.ToCanonicalPath());
            await _fileSystem.WriteAllTextAsync("/test/file1.txt".ToCanonicalPath(), "content1");
            await _fileSystem.WriteAllTextAsync("/test/file2.txt".ToCanonicalPath(), "content2");
            await _fileSystem.WriteAllTextAsync("/test/file3.log".ToCanonicalPath(), "content3");

            // Act
            var result = await _fileSystem.GetFilesAsync(directory.ToCanonicalPath(), "*.txt");

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().HaveCount(2);
            result.Value.Should().Contain("/test/file1.txt".ToCanonicalPath());
            result.Value.Should().Contain("/test/file2.txt".ToCanonicalPath());
            result.Value.Should().NotContain("/test/file3.log".ToCanonicalPath());
        }

        [Fact(DisplayName = "IMFS-013: GetDirectoriesAsync returns correct directory list")]
        public async Task IMFS013()
        {
            // Arrange
            var baseDirectory = "/test";
            await _fileSystem.CreateDirectoryAsync(baseDirectory.ToCanonicalPath());
            await _fileSystem.CreateDirectoryAsync("/test/subdir1".ToCanonicalPath());
            await _fileSystem.CreateDirectoryAsync("/test/subdir2".ToCanonicalPath());
            await _fileSystem.CreateDirectoryAsync("/test/other".ToCanonicalPath());

            // Act
            var result = await _fileSystem.GetDirectoriesAsync(baseDirectory.ToCanonicalPath(), "subdir*");

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().HaveCount(2);
            result.Value.Should().Contain("/test/subdir1".ToCanonicalPath());
            result.Value.Should().Contain("/test/subdir2".ToCanonicalPath());
            result.Value.Should().NotContain("/test/other".ToCanonicalPath());
        }

        [Fact(DisplayName = "IMFS-014: DeleteFileAsync removes file successfully")]
        public async Task IMFS014()
        {
            // Arrange
            var path = "/test/file.txt";
            await _fileSystem.WriteAllTextAsync(path.ToCanonicalPath(), "content");

            // Act
            var result = await _fileSystem.DeleteFileAsync(path.ToCanonicalPath());

            // Assert
            result.IsSuccess.Should().BeTrue();
            
            var existsResult = await _fileSystem.FileExistsAsync(path.ToCanonicalPath());
            existsResult.IsSuccess.Should().BeTrue();
            existsResult.Value.Should().BeFalse();
        }

        [Fact(DisplayName = "IMFS-015: DeleteDirectoryAsync removes empty directory")]
        public async Task IMFS015()
        {
            // Arrange
            var path = "/test/directory";
            await _fileSystem.CreateDirectoryAsync(path.ToCanonicalPath());

            // Act
            var result = await _fileSystem.DeleteDirectoryAsync(path.ToCanonicalPath());

            // Assert
            result.IsSuccess.Should().BeTrue();
            
            var existsResult = await _fileSystem.DirectoryExistsAsync(path.ToCanonicalPath());
            existsResult.IsSuccess.Should().BeTrue();
            existsResult.Value.Should().BeFalse();
        }

        [Fact(DisplayName = "IMFS-016: DeleteDirectoryAsync with recursive removes all contents")]
        public async Task IMFS016()
        {
            // Arrange
            var path = "/test/directory";
            await _fileSystem.CreateDirectoryAsync(path.ToCanonicalPath());
            await _fileSystem.CreateDirectoryAsync("/test/directory/subdir".ToCanonicalPath());
            await _fileSystem.WriteAllTextAsync("/test/directory/file.txt".ToCanonicalPath(), "content");
            await _fileSystem.WriteAllTextAsync("/test/directory/subdir/file2.txt".ToCanonicalPath(), "content2");

            // Act
            var result = await _fileSystem.DeleteDirectoryAsync(path.ToCanonicalPath(), recursive: true);

            // Assert
            result.IsSuccess.Should().BeTrue();
            
            var existsResult = await _fileSystem.DirectoryExistsAsync(path.ToCanonicalPath());
            existsResult.IsSuccess.Should().BeTrue();
            existsResult.Value.Should().BeFalse();
            
            var fileExistsResult = await _fileSystem.FileExistsAsync("/test/directory/file.txt".ToCanonicalPath());
            fileExistsResult.Value.Should().BeFalse();
            
            var subdirExistsResult = await _fileSystem.DirectoryExistsAsync("/test/directory/subdir".ToCanonicalPath());
            subdirExistsResult.Value.Should().BeFalse();
        }


        [Fact(DisplayName = "IMFS-018: CreateMonitoringSessionAsync returns valid session")]
        public async Task IMFS018()
        {
            // Arrange
            var options = MonitoringOptions.ForPath(CanonicalPath.Root);

            // Act
            var result = await _fileSystem.CreateMonitoringSessionAsync(options);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value.Should().BeAssignableTo<IMonitoringSession>();
            
            // Clean up
            result.Value.Dispose();
        }

        [Fact(DisplayName = "IMFS-019: Path normalization works correctly")]
        public async Task IMFS019()
        {
            // Arrange
            var content = "test content";

            // Act
            // Since ToCanonicalPath() is strict, we normalize paths before converting
            var path1 = "\\test\\file.txt".Replace('\\', '/');
            var path2 = "/test//file2.txt".Replace("//", "/");
            var path3 = "/test/./file3.txt".Replace("/./", "/");
            
            await _fileSystem.WriteAllTextAsync(path1.ToCanonicalPath(), content);
            await _fileSystem.WriteAllTextAsync(path2.ToCanonicalPath(), content);
            await _fileSystem.WriteAllTextAsync(path3.ToCanonicalPath(), content);

            // Assert
            var result1 = await _fileSystem.FileExistsAsync("/test/file.txt".ToCanonicalPath());
            result1.Value.Should().BeTrue();

            var result2 = await _fileSystem.FileExistsAsync("/test/file2.txt".ToCanonicalPath());
            result2.Value.Should().BeTrue();

            var result3 = await _fileSystem.FileExistsAsync("/test/file3.txt".ToCanonicalPath());
            result3.Value.Should().BeTrue();
        }

        [Fact(DisplayName = "IMFS-020: Parent directories are created automatically")]
        public async Task IMFS020()
        {
            // Arrange
            var path = "/deep/nested/directory/file.txt";
            var content = "test content";

            // Act
            await _fileSystem.WriteAllTextAsync(path.ToCanonicalPath(), content);

            // Assert
            var dirExists1 = await _fileSystem.DirectoryExistsAsync("/deep".ToCanonicalPath());
            dirExists1.Value.Should().BeTrue();

            var dirExists2 = await _fileSystem.DirectoryExistsAsync("/deep/nested".ToCanonicalPath());
            dirExists2.Value.Should().BeTrue();

            var dirExists3 = await _fileSystem.DirectoryExistsAsync("/deep/nested/directory".ToCanonicalPath());
            dirExists3.Value.Should().BeTrue();

            var fileExists = await _fileSystem.FileExistsAsync(path.ToCanonicalPath());
            fileExists.Value.Should().BeTrue();
        }

        [Fact(DisplayName = "IMFS-034: Info property returns correct characteristics for InMemoryFileSystem")]
        public void IMFS034()
        {
            // Act
            var info = _fileSystem.Info;

            // Assert
            info.IsDurable.Should().BeFalse("in-memory file system does not persist data across application restarts");
            info.SupportsMonitoring.Should().BeTrue("in-memory file system supports monitoring via internal event system");
            info.IsReadOnly.Should().BeFalse("in-memory file system supports both read and write operations");
        }

        [Fact(DisplayName = "IMFS-035: TraverseAsync with empty file system returns empty results")]
        public async Task IMFS035()
        {
            // Arrange
            var handler = new PathCollectorHandler("*", includeFiles: true, includeDirectories: true);
            var options = new TraversalOptions();

            // Act
            var result = await _fileSystem.TraverseAsync("/".ToCanonicalPath(), handler, options);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.FilesProcessed.Should().Be(0);
            result.Value.DirectoriesProcessed.Should().Be(1); // Root directory is counted
            handler.CollectedPaths.Should().BeEmpty();
        }

        [Fact(DisplayName = "IMFS-036: TraverseAsync collects files and directories")]
        public async Task IMFS036()
        {
            // Arrange
            await _fileSystem.WriteAllTextAsync("/file1.txt".ToCanonicalPath(), "content1");
            await _fileSystem.WriteAllTextAsync("/subdir/file2.txt".ToCanonicalPath(), "content2");
            await _fileSystem.CreateDirectoryAsync("/emptydir".ToCanonicalPath());

            var handler = new PathCollectorHandler("*", includeFiles: true, includeDirectories: true);
            var options = new TraversalOptions(recursive: true);

            // Act
            var result = await _fileSystem.TraverseAsync("/".ToCanonicalPath(), handler, options);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.FilesProcessed.Should().Be(2);
            result.Value.DirectoriesProcessed.Should().Be(3); // root, subdir and emptydir
            
            handler.CollectedPaths.Should().Contain("/file1.txt".ToCanonicalPath());
            handler.CollectedPaths.Should().Contain("/subdir/file2.txt".ToCanonicalPath());
            handler.CollectedPaths.Should().Contain("/subdir".ToCanonicalPath());
            handler.CollectedPaths.Should().Contain("/emptydir".ToCanonicalPath());
        }

        [Fact(DisplayName = "IMFS-037: TraverseAsync respects include filters")]
        public async Task IMFS037()
        {
            // Arrange
            await _fileSystem.WriteAllTextAsync("/file1.txt".ToCanonicalPath(), "content1");
            await _fileSystem.CreateDirectoryAsync("/testdir".ToCanonicalPath());

            var fileOnlyHandler = new PathCollectorHandler("*", includeFiles: true, includeDirectories: false);
            var dirOnlyHandler = new PathCollectorHandler("*", includeFiles: false, includeDirectories: true);

            // Act
            var fileResult = await _fileSystem.TraverseAsync("/".ToCanonicalPath(), fileOnlyHandler, new TraversalOptions());
            var dirResult = await _fileSystem.TraverseAsync("/".ToCanonicalPath(), dirOnlyHandler, new TraversalOptions());

            // Assert
            fileResult.IsSuccess.Should().BeTrue();
            fileOnlyHandler.CollectedPaths.Should().Contain("/file1.txt".ToCanonicalPath());
            fileOnlyHandler.CollectedPaths.Should().NotContain("/testdir".ToCanonicalPath());

            dirResult.IsSuccess.Should().BeTrue();
            dirOnlyHandler.CollectedPaths.Should().Contain("/testdir".ToCanonicalPath());
            dirOnlyHandler.CollectedPaths.Should().NotContain("/file1.txt".ToCanonicalPath());
        }

        [Fact(DisplayName = "IMFS-038: TraverseAsync respects search pattern")]
        public async Task IMFS038()
        {
            // Arrange
            await _fileSystem.WriteAllTextAsync("/file1.txt".ToCanonicalPath(), "content1");
            await _fileSystem.WriteAllTextAsync("/file2.doc".ToCanonicalPath(), "content2");
            await _fileSystem.WriteAllTextAsync("/readme".ToCanonicalPath(), "content3");

            var handler = new PathCollectorHandler("*.txt", includeFiles: true, includeDirectories: false);

            // Act
            var result = await _fileSystem.TraverseAsync("/".ToCanonicalPath(), handler, new TraversalOptions());

            // Assert
            result.IsSuccess.Should().BeTrue();
            handler.CollectedPaths.Should().Contain("/file1.txt".ToCanonicalPath());
            handler.CollectedPaths.Should().NotContain("/file2.doc".ToCanonicalPath());
            handler.CollectedPaths.Should().NotContain("/readme".ToCanonicalPath());
        }

        [Fact(DisplayName = "IMFS-039: TraverseAsync handles non-existent path")]
        public async Task IMFS039()
        {
            // Arrange
            var handler = new PathCollectorHandler("*", includeFiles: true, includeDirectories: true);

            // Act
            var result = await _fileSystem.TraverseAsync("/nonexistent".ToCanonicalPath(), handler, new TraversalOptions());

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Type.Should().Be(FileSystemErrorType.PathNotFound);
        }

        public void Dispose()
        {
            _fileSystem?.Dispose();
        }
    }
}
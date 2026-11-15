using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Local.PathMappers;
using FluentAssertions;
using Xunit;

namespace PlatformTests.Local;

/// <summary>
/// Tests that verify special file operations work correctly on all platforms.
/// These include symlinks (where supported), recursive operations, and edge cases.
/// </summary>
public class SpecialFileTests : PlatformTestBase
{
    private IPathMapper GetPlatformMapper() => IsWindows ? new WindowsPathMapper() : new UnixPathMapper();

    [SkippableFact(DisplayName = "PSS-001: Symlink to file can be read through")]
    public async Task PSS001()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var tempDir = Path.Combine(Path.GetTempPath(), $"symlink_test_{Guid.NewGuid()}");
        var targetFile = Path.Combine(tempDir, "target.txt");
        var linkFile = Path.Combine(tempDir, "link.txt");

        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(targetFile, "Target content");

            // Try to create symlink - may fail without permissions
            try
            {
                File.CreateSymbolicLink(linkFile, targetFile);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                Skip.If(true, $"Symlink creation not available on {CurrentPlatform}: {ex.Message}");
            }

            var linkCanonical = mapper.MapToCanonicalPath(linkFile).Value;

            // Act
            var result = await fileSystem.ReadAllTextAsync(linkCanonical);

            // Assert
            result.IsSuccess.Should().BeTrue($"reading through symlink should succeed on {CurrentPlatform}");
            result.Value.Should().Be("Target content");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [SkippableFact(DisplayName = "PSS-002: Symlink to directory can be traversed")]
    public async Task PSS002()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var tempDir = Path.Combine(Path.GetTempPath(), $"dirsymlink_test_{Guid.NewGuid()}");
        var targetDir = Path.Combine(tempDir, "target");
        var linkDir = Path.Combine(tempDir, "link");

        try
        {
            Directory.CreateDirectory(targetDir);
            await File.WriteAllTextAsync(Path.Combine(targetDir, "file.txt"), "Content via link");

            try
            {
                Directory.CreateSymbolicLink(linkDir, targetDir);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                Skip.If(true, $"Directory symlink creation not available on {CurrentPlatform}: {ex.Message}");
            }

            var fileViaLink = Path.Combine(linkDir, "file.txt");
            var canonicalPath = mapper.MapToCanonicalPath(fileViaLink).Value;

            // Act
            var result = await fileSystem.ReadAllTextAsync(canonicalPath);

            // Assert
            result.IsSuccess.Should().BeTrue($"reading through directory symlink should succeed on {CurrentPlatform}");
            result.Value.Should().Be("Content via link");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact(DisplayName = "PSS-003: Recursive file listing works correctly")]
    public async Task PSS003()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var tempDir = Path.Combine(Path.GetTempPath(), $"recursive_test_{Guid.NewGuid()}");

        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "root.txt"), "1");
            Directory.CreateDirectory(Path.Combine(tempDir, "level1"));
            await File.WriteAllTextAsync(Path.Combine(tempDir, "level1", "file1.txt"), "2");
            Directory.CreateDirectory(Path.Combine(tempDir, "level1", "level2"));
            await File.WriteAllTextAsync(Path.Combine(tempDir, "level1", "level2", "file2.txt"), "3");

            var canonicalPath = mapper.MapToCanonicalPath(tempDir).Value;

            // Act
            var result = await fileSystem.GetFilesAsync(canonicalPath, "*", recursive: true);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().HaveCount(3, "should find all files recursively");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact(DisplayName = "PSS-004: Delete file removes file from filesystem")]
    public async Task PSS004()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var tempFile = Path.Combine(Path.GetTempPath(), $"delete_test_{Guid.NewGuid()}.txt");

        try
        {
            await File.WriteAllTextAsync(tempFile, "To be deleted");
            var canonicalPath = mapper.MapToCanonicalPath(tempFile).Value;

            // Act
            var result = await fileSystem.DeleteFileAsync(canonicalPath);

            // Assert
            result.IsSuccess.Should().BeTrue($"delete should succeed on {CurrentPlatform}");
            File.Exists(tempFile).Should().BeFalse("file should be removed");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact(DisplayName = "PSS-005: Delete directory removes directory and contents")]
    public async Task PSS005()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var tempDir = Path.Combine(Path.GetTempPath(), $"delete_dir_{Guid.NewGuid()}");

        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "file.txt"), "content");
            Directory.CreateDirectory(Path.Combine(tempDir, "subdir"));

            var canonicalPath = mapper.MapToCanonicalPath(tempDir).Value;

            // Act
            var result = await fileSystem.DeleteDirectoryAsync(canonicalPath, recursive: true);

            // Assert
            result.IsSuccess.Should().BeTrue($"delete directory should succeed on {CurrentPlatform}");
            Directory.Exists(tempDir).Should().BeFalse("directory should be removed");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact(DisplayName = "PSS-006: FileExistsAsync returns false for directories")]
    public async Task PSS006()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var tempDir = Path.Combine(Path.GetTempPath(), $"exists_test_{Guid.NewGuid()}");

        try
        {
            Directory.CreateDirectory(tempDir);
            var canonicalPath = mapper.MapToCanonicalPath(tempDir).Value;

            // Act
            var fileExists = await fileSystem.FileExistsAsync(canonicalPath);
            var dirExists = await fileSystem.DirectoryExistsAsync(canonicalPath);

            // Assert
            fileExists.Value.Should().BeFalse("directory should not be reported as file");
            dirExists.Value.Should().BeTrue("directory should be reported as directory");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir);
        }
    }

    [Fact(DisplayName = "PSS-007: DirectoryExistsAsync returns false for files")]
    public async Task PSS007()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var tempFile = Path.Combine(Path.GetTempPath(), $"exists_file_{Guid.NewGuid()}.txt");

        try
        {
            await File.WriteAllTextAsync(tempFile, "content");
            var canonicalPath = mapper.MapToCanonicalPath(tempFile).Value;

            // Act
            var fileExists = await fileSystem.FileExistsAsync(canonicalPath);
            var dirExists = await fileSystem.DirectoryExistsAsync(canonicalPath);

            // Assert
            fileExists.Value.Should().BeTrue("file should be reported as file");
            dirExists.Value.Should().BeFalse("file should not be reported as directory");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact(DisplayName = "PSS-008: CreateDirectoryAsync creates nested directories")]
    public async Task PSS008()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var deepPath = Path.Combine(Path.GetTempPath(), $"nested_{Guid.NewGuid()}", "level1", "level2", "level3");

        try
        {
            var canonicalPath = mapper.MapToCanonicalPath(deepPath).Value;

            // Act
            var result = await fileSystem.CreateDirectoryAsync(canonicalPath);

            // Assert
            result.IsSuccess.Should().BeTrue($"creating nested directories should succeed on {CurrentPlatform}");
            Directory.Exists(deepPath).Should().BeTrue("deep directory should exist");
        }
        finally
        {
            var root = Path.Combine(Path.GetTempPath(), deepPath.Split(Path.DirectorySeparatorChar)
                .SkipWhile(s => !s.StartsWith("nested_")).First());
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}

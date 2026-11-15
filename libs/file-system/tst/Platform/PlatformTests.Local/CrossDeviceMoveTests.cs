using System;
using System.IO;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Local.PathMappers;
using FluentAssertions;
using Xunit;

namespace PlatformTests.Local;

/// <summary>
/// Tests that verify file and directory move operations work correctly on all platforms.
/// The library should handle cross-device moves transparently via copy-delete fallback.
/// </summary>
public class CrossDeviceMoveTests : PlatformTestBase
{
    private IPathMapper GetPlatformMapper() => IsWindows ? new WindowsPathMapper() : new UnixPathMapper();

    [Fact(DisplayName = "PSM-001: File move succeeds within same directory")]
    public async Task PSM001()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var tempDir = Path.GetTempPath();
        var sourceFile = Path.Combine(tempDir, $"move_source_{Guid.NewGuid()}.txt");
        var destFile = Path.Combine(tempDir, $"move_dest_{Guid.NewGuid()}.txt");

        try
        {
            await File.WriteAllTextAsync(sourceFile, "Move test content");

            var sourceCanonical = mapper.MapToCanonicalPath(sourceFile).Value;
            var destCanonical = mapper.MapToCanonicalPath(destFile).Value;

            // Act
            var result = await fileSystem.MoveFileAsync(sourceCanonical, destCanonical);

            // Assert
            result.IsSuccess.Should().BeTrue($"file move should succeed on {CurrentPlatform}");
            File.Exists(destFile).Should().BeTrue("destination file should exist");
            File.Exists(sourceFile).Should().BeFalse("source file should be deleted");
            (await File.ReadAllTextAsync(destFile)).Should().Be("Move test content");
        }
        finally
        {
            if (File.Exists(sourceFile)) File.Delete(sourceFile);
            if (File.Exists(destFile)) File.Delete(destFile);
        }
    }

    [Fact(DisplayName = "PSM-002: File move succeeds to different directory")]
    public async Task PSM002()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var sourceDir = Path.Combine(Path.GetTempPath(), $"src_{Guid.NewGuid()}");
        var destDir = Path.Combine(Path.GetTempPath(), $"dest_{Guid.NewGuid()}");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destDir);

        var sourceFile = Path.Combine(sourceDir, "file.txt");
        var destFile = Path.Combine(destDir, "file.txt");

        try
        {
            await File.WriteAllTextAsync(sourceFile, "Content to move");

            var sourceCanonical = mapper.MapToCanonicalPath(sourceFile).Value;
            var destCanonical = mapper.MapToCanonicalPath(destFile).Value;

            // Act
            var result = await fileSystem.MoveFileAsync(sourceCanonical, destCanonical);

            // Assert
            result.IsSuccess.Should().BeTrue($"file move to different directory should succeed on {CurrentPlatform}");
            File.Exists(destFile).Should().BeTrue("destination file should exist");
            File.Exists(sourceFile).Should().BeFalse("source file should be deleted");
        }
        finally
        {
            if (Directory.Exists(sourceDir)) Directory.Delete(sourceDir, true);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
        }
    }

    [Fact(DisplayName = "PSM-003: Directory move succeeds")]
    public async Task PSM003()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var sourceDir = Path.Combine(Path.GetTempPath(), $"move_dir_src_{Guid.NewGuid()}");
        var destDir = Path.Combine(Path.GetTempPath(), $"move_dir_dest_{Guid.NewGuid()}");

        try
        {
            // Create source directory with files
            Directory.CreateDirectory(sourceDir);
            await File.WriteAllTextAsync(Path.Combine(sourceDir, "file1.txt"), "Content 1");
            await File.WriteAllTextAsync(Path.Combine(sourceDir, "file2.txt"), "Content 2");
            Directory.CreateDirectory(Path.Combine(sourceDir, "subdir"));
            await File.WriteAllTextAsync(Path.Combine(sourceDir, "subdir", "nested.txt"), "Nested");

            var sourceCanonical = mapper.MapToCanonicalPath(sourceDir).Value;
            var destCanonical = mapper.MapToCanonicalPath(destDir).Value;

            // Act
            var result = await fileSystem.MoveDirectoryAsync(sourceCanonical, destCanonical);

            // Assert
            result.IsSuccess.Should().BeTrue($"directory move should succeed on {CurrentPlatform}");
            Directory.Exists(destDir).Should().BeTrue("destination directory should exist");
            Directory.Exists(sourceDir).Should().BeFalse("source directory should be deleted");
            File.ReadAllText(Path.Combine(destDir, "file1.txt")).Should().Be("Content 1");
            File.ReadAllText(Path.Combine(destDir, "subdir", "nested.txt")).Should().Be("Nested");
        }
        finally
        {
            if (Directory.Exists(sourceDir)) Directory.Delete(sourceDir, true);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
        }
    }

    [Fact(DisplayName = "PSM-004: Move preserves file content integrity")]
    public async Task PSM004()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var sourceFile = Path.Combine(Path.GetTempPath(), $"integrity_src_{Guid.NewGuid()}.bin");
        var destFile = Path.Combine(Path.GetTempPath(), $"integrity_dest_{Guid.NewGuid()}.bin");

        var content = new byte[1024 * 100]; // 100KB
        new Random(42).NextBytes(content);

        try
        {
            await File.WriteAllBytesAsync(sourceFile, content);

            var sourceCanonical = mapper.MapToCanonicalPath(sourceFile).Value;
            var destCanonical = mapper.MapToCanonicalPath(destFile).Value;

            // Act
            var result = await fileSystem.MoveFileAsync(sourceCanonical, destCanonical);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var destContent = await File.ReadAllBytesAsync(destFile);
            destContent.Should().BeEquivalentTo(content, "content should be preserved exactly after move");
        }
        finally
        {
            if (File.Exists(sourceFile)) File.Delete(sourceFile);
            if (File.Exists(destFile)) File.Delete(destFile);
        }
    }

    [Fact(DisplayName = "PSM-005: Copy file preserves content")]
    public async Task PSM005()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var sourceFile = Path.Combine(Path.GetTempPath(), $"copy_src_{Guid.NewGuid()}.txt");
        var destFile = Path.Combine(Path.GetTempPath(), $"copy_dest_{Guid.NewGuid()}.txt");

        try
        {
            await File.WriteAllTextAsync(sourceFile, "Content to copy");

            var sourceCanonical = mapper.MapToCanonicalPath(sourceFile).Value;
            var destCanonical = mapper.MapToCanonicalPath(destFile).Value;

            // Act
            var result = await fileSystem.CopyFileAsync(sourceCanonical, destCanonical);

            // Assert
            result.IsSuccess.Should().BeTrue($"copy should succeed on {CurrentPlatform}");
            File.Exists(sourceFile).Should().BeTrue("source file should still exist after copy");
            File.Exists(destFile).Should().BeTrue("destination file should exist");
            (await File.ReadAllTextAsync(destFile)).Should().Be("Content to copy");
        }
        finally
        {
            if (File.Exists(sourceFile)) File.Delete(sourceFile);
            if (File.Exists(destFile)) File.Delete(destFile);
        }
    }

    [Fact(DisplayName = "PSM-006: Copy directory preserves structure and content")]
    public async Task PSM006()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var sourceDir = Path.Combine(Path.GetTempPath(), $"copy_dir_src_{Guid.NewGuid()}");
        var destDir = Path.Combine(Path.GetTempPath(), $"copy_dir_dest_{Guid.NewGuid()}");

        try
        {
            Directory.CreateDirectory(sourceDir);
            await File.WriteAllTextAsync(Path.Combine(sourceDir, "file.txt"), "File content");
            Directory.CreateDirectory(Path.Combine(sourceDir, "subdir"));
            await File.WriteAllTextAsync(Path.Combine(sourceDir, "subdir", "nested.txt"), "Nested content");

            var sourceCanonical = mapper.MapToCanonicalPath(sourceDir).Value;
            var destCanonical = mapper.MapToCanonicalPath(destDir).Value;

            // Act
            var result = await fileSystem.CopyDirectoryAsync(sourceCanonical, destCanonical);

            // Assert
            result.IsSuccess.Should().BeTrue($"directory copy should succeed on {CurrentPlatform}");
            Directory.Exists(sourceDir).Should().BeTrue("source should still exist after copy");
            Directory.Exists(destDir).Should().BeTrue("destination should exist");
            File.ReadAllText(Path.Combine(destDir, "file.txt")).Should().Be("File content");
            File.ReadAllText(Path.Combine(destDir, "subdir", "nested.txt")).Should().Be("Nested content");
        }
        finally
        {
            if (Directory.Exists(sourceDir)) Directory.Delete(sourceDir, true);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
        }
    }

    [Fact(DisplayName = "PSM-007: Move creates parent directories if needed")]
    public async Task PSM007()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var sourceFile = Path.Combine(Path.GetTempPath(), $"move_parent_src_{Guid.NewGuid()}.txt");
        var parentDir = Path.Combine(Path.GetTempPath(), $"newparent_{Guid.NewGuid()}");
        var destFile = Path.Combine(parentDir, "file.txt");

        try
        {
            await File.WriteAllTextAsync(sourceFile, "Content");
            Directory.Exists(parentDir).Should().BeFalse("parent should not exist before move");

            var sourceCanonical = mapper.MapToCanonicalPath(sourceFile).Value;
            var destCanonical = mapper.MapToCanonicalPath(destFile).Value;

            // Act
            var result = await fileSystem.MoveFileAsync(sourceCanonical, destCanonical);

            // Assert - library creates parent directories automatically
            result.IsSuccess.Should().BeTrue($"move should create parent directories on {CurrentPlatform}");
            File.Exists(destFile).Should().BeTrue("file should exist at destination");
            File.Exists(sourceFile).Should().BeFalse("source should be removed");
        }
        finally
        {
            if (File.Exists(sourceFile)) File.Delete(sourceFile);
            if (Directory.Exists(parentDir)) Directory.Delete(parentDir, true);
        }
    }

    [Fact(DisplayName = "PSM-008: Move non-existent file fails gracefully")]
    public async Task PSM008()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var sourceFile = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.txt");
        var destFile = Path.Combine(Path.GetTempPath(), $"dest_{Guid.NewGuid()}.txt");

        var sourceCanonical = mapper.MapToCanonicalPath(sourceFile).Value;
        var destCanonical = mapper.MapToCanonicalPath(destFile).Value;

        // Act
        var result = await fileSystem.MoveFileAsync(sourceCanonical, destCanonical);

        // Assert
        result.IsFailure.Should().BeTrue("moving non-existent file should fail gracefully");
    }
}

using System;
using System.IO;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Local.PathMappers;
using Ave.Extensions.FileSystem.Mapping;
using FluentAssertions;
using Xunit;

namespace PlatformTests.Local;

/// <summary>
/// Tests that verify file metadata and attribute handling works correctly on all platforms.
/// The library should provide consistent access to file information regardless of OS.
/// </summary>
public class FileAttributeTests : PlatformTestBase
{
    private IPathMapper GetPlatformMapper() => IsWindows ? new WindowsPathMapper() : new UnixPathMapper();

    [Fact(DisplayName = "PSA-001: GetEntryAsync returns correct file metadata")]
    public async Task PSA001()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var tempFile = Path.Combine(Path.GetTempPath(), $"metadata_test_{Guid.NewGuid()}.txt");
        var content = "Test content for metadata";

        try
        {
            await File.WriteAllTextAsync(tempFile, content);
            var canonicalPath = mapper.MapToCanonicalPath(tempFile).Value;

            // Act
            var result = await fileSystem.GetEntryAsync(canonicalPath, IdentityMapper.Instance);

            // Assert
            result.IsSuccess.Should().BeTrue($"GetEntryAsync should succeed on {CurrentPlatform}");
            result.Value.IsDirectory.Should().BeFalse("file should not be a directory");
            result.Value.Size.Should().BeGreaterThan(0, "file size should be reported");
            result.Value.Name.Should().Contain("metadata_test_");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact(DisplayName = "PSA-002: GetEntryAsync returns correct directory metadata")]
    public async Task PSA002()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var tempDir = Path.Combine(Path.GetTempPath(), $"dir_metadata_{Guid.NewGuid()}");

        try
        {
            Directory.CreateDirectory(tempDir);
            var canonicalPath = mapper.MapToCanonicalPath(tempDir).Value;

            // Act
            var result = await fileSystem.GetEntryAsync(canonicalPath, IdentityMapper.Instance);

            // Assert
            result.IsSuccess.Should().BeTrue($"GetEntryAsync should succeed on {CurrentPlatform}");
            result.Value.IsDirectory.Should().BeTrue("directory should be marked as directory");
            result.Value.Attributes.HasFlag(FileAttributes.Directory).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir);
        }
    }

    [Fact(DisplayName = "PSA-003: File timestamps are accurate")]
    public async Task PSA003()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var tempFile = Path.Combine(Path.GetTempPath(), $"timestamp_{Guid.NewGuid()}.txt");

        try
        {
            var beforeCreate = DateTime.UtcNow.AddSeconds(-1);
            await File.WriteAllTextAsync(tempFile, "Timestamp test");
            var afterCreate = DateTime.UtcNow.AddSeconds(1);

            var canonicalPath = mapper.MapToCanonicalPath(tempFile).Value;

            // Act
            var result = await fileSystem.GetEntryAsync(canonicalPath, IdentityMapper.Instance);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.ModifiedUtc.Should().BeAfter(beforeCreate, "modification time should be recent");
            result.Value.ModifiedUtc.Should().BeBefore(afterCreate, "modification time should not be in future");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact(DisplayName = "PSA-004: File size is reported correctly")]
    public async Task PSA004()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var tempFile = Path.Combine(Path.GetTempPath(), $"size_test_{Guid.NewGuid()}.txt");
        var content = "Hello, World! This is test content with known size.";

        try
        {
            await File.WriteAllTextAsync(tempFile, content);
            var expectedSize = new FileInfo(tempFile).Length;
            var canonicalPath = mapper.MapToCanonicalPath(tempFile).Value;

            // Act
            var result = await fileSystem.GetEntryAsync(canonicalPath, IdentityMapper.Instance);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Size.Should().Be(expectedSize, "reported size should match actual file size");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact(DisplayName = "PSA-005: Empty file has zero size")]
    public async Task PSA005()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var tempFile = Path.Combine(Path.GetTempPath(), $"empty_{Guid.NewGuid()}.txt");

        try
        {
            await File.WriteAllTextAsync(tempFile, "");
            var canonicalPath = mapper.MapToCanonicalPath(tempFile).Value;

            // Act
            var result = await fileSystem.GetEntryAsync(canonicalPath, IdentityMapper.Instance);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Size.Should().Be(0, "empty file should have zero size");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact(DisplayName = "PSA-006: GetFilesAsync returns files in directory")]
    public async Task PSA006()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var tempDir = Path.Combine(Path.GetTempPath(), $"list_files_{Guid.NewGuid()}");

        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "file1.txt"), "1");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "file2.txt"), "2");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "file3.log"), "3");

            var canonicalPath = mapper.MapToCanonicalPath(tempDir).Value;

            // Act
            var result = await fileSystem.GetFilesAsync(canonicalPath, "*", recursive: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().HaveCount(3, "should list all 3 files");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact(DisplayName = "PSA-007: GetFilesAsync supports pattern filtering")]
    public async Task PSA007()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var tempDir = Path.Combine(Path.GetTempPath(), $"filter_files_{Guid.NewGuid()}");

        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "file1.txt"), "1");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "file2.txt"), "2");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "file3.log"), "3");

            var canonicalPath = mapper.MapToCanonicalPath(tempDir).Value;

            // Act
            var result = await fileSystem.GetFilesAsync(canonicalPath, "*.txt", recursive: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().HaveCount(2, "should only list .txt files");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact(DisplayName = "PSA-008: GetDirectoriesAsync returns subdirectories")]
    public async Task PSA008()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var tempDir = Path.Combine(Path.GetTempPath(), $"list_dirs_{Guid.NewGuid()}");

        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "subdir1"));
            Directory.CreateDirectory(Path.Combine(tempDir, "subdir2"));
            await File.WriteAllTextAsync(Path.Combine(tempDir, "file.txt"), "not a dir");

            var canonicalPath = mapper.MapToCanonicalPath(tempDir).Value;

            // Act
            var result = await fileSystem.GetDirectoriesAsync(canonicalPath, "*", recursive: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().HaveCount(2, "should list only directories, not files");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}

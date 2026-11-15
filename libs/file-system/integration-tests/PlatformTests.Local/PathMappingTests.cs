using System.IO;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Local.PathMappers;
using FluentAssertions;
using Xunit;

namespace PlatformTests.Local;

/// <summary>
/// Tests that verify path mapping abstraction works correctly on all platforms.
/// The library should transparently convert between native paths and canonical paths.
/// </summary>
public class PathMappingTests : PlatformTestBase
{
    private IPathMapper GetPlatformMapper() => IsWindows ? new WindowsPathMapper() : new UnixPathMapper();

    [Fact(DisplayName = "PSP-001: Path mapper round-trip preserves paths")]
    public void PSP001()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var nativePath = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

        // Act - round trip: native -> canonical -> native
        var toCanonical = mapper.MapToCanonicalPath(nativePath);
        toCanonical.IsSuccess.Should().BeTrue($"mapping to canonical should succeed on {CurrentPlatform}");

        var backToNative = mapper.MapToSystemPath(toCanonical.Value);
        backToNative.IsSuccess.Should().BeTrue($"mapping back to native should succeed on {CurrentPlatform}");

        // Assert - should get back the original path
        backToNative.Value.Should().Be(nativePath, "round-trip should preserve the path");
    }

    [Fact(DisplayName = "PSP-002: Canonical paths are always forward-slash separated")]
    public void PSP002()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var nativePath = Path.Combine(Path.GetTempPath(), "subdir", "file.txt");

        // Act
        var canonical = mapper.MapToCanonicalPath(nativePath);

        // Assert
        canonical.IsSuccess.Should().BeTrue();
        canonical.Value.Value.Should().NotContain("\\", "canonical paths should use forward slashes only");
        canonical.Value.Value.Should().StartWith("/", "canonical paths should start with /");
    }

    [Fact(DisplayName = "PSP-003: LocalFileSystem can read/write using canonical paths")]
    public async Task PSP003()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var nativeTempFile = Path.Combine(Path.GetTempPath(), $"path_test_{System.Guid.NewGuid()}.txt");
        var content = "Test content for path mapping";

        try
        {
            var canonicalPath = mapper.MapToCanonicalPath(nativeTempFile).Value;

            // Act - write and read using canonical path
            var writeResult = await fileSystem.WriteAllTextAsync(canonicalPath, content);
            writeResult.IsSuccess.Should().BeTrue($"write should succeed on {CurrentPlatform}");

            var readResult = await fileSystem.ReadAllTextAsync(canonicalPath);

            // Assert
            readResult.IsSuccess.Should().BeTrue($"read should succeed on {CurrentPlatform}");
            readResult.Value.Should().Be(content);

            // Verify file exists at native path too
            File.Exists(nativeTempFile).Should().BeTrue("file should exist at native path");
        }
        finally
        {
            if (File.Exists(nativeTempFile)) File.Delete(nativeTempFile);
        }
    }

    [Fact(DisplayName = "PSP-004: LocalFileSystem directory operations work with canonical paths")]
    public async Task PSP004()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var nativeTempDir = Path.Combine(Path.GetTempPath(), $"dir_test_{System.Guid.NewGuid()}");

        try
        {
            var canonicalPath = mapper.MapToCanonicalPath(nativeTempDir).Value;

            // Act - create directory using canonical path
            var createResult = await fileSystem.CreateDirectoryAsync(canonicalPath);
            createResult.IsSuccess.Should().BeTrue($"create directory should succeed on {CurrentPlatform}");

            var existsResult = await fileSystem.DirectoryExistsAsync(canonicalPath);

            // Assert
            existsResult.IsSuccess.Should().BeTrue();
            existsResult.Value.Should().BeTrue("directory should exist");
            Directory.Exists(nativeTempDir).Should().BeTrue("directory should exist at native path");
        }
        finally
        {
            if (Directory.Exists(nativeTempDir)) Directory.Delete(nativeTempDir, true);
        }
    }

    [Fact(DisplayName = "PSP-005: Path mapper rejects empty and null paths")]
    public void PSP005()
    {
        // Arrange
        var mapper = GetPlatformMapper();

        // Act & Assert
        mapper.MapToCanonicalPath("").IsFailure.Should().BeTrue("empty path should fail");
        mapper.MapToCanonicalPath("   ").IsFailure.Should().BeTrue("whitespace path should fail");
        mapper.MapToCanonicalPath(null!).IsFailure.Should().BeTrue("null path should fail");
    }

    [Fact(DisplayName = "PSP-006: Temp directory is accessible via canonical path")]
    public async Task PSP006()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var nativeTempDir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var canonicalPath = mapper.MapToCanonicalPath(nativeTempDir).Value;

        // Act
        var existsResult = await fileSystem.DirectoryExistsAsync(canonicalPath);

        // Assert
        existsResult.IsSuccess.Should().BeTrue();
        existsResult.Value.Should().BeTrue($"temp directory should be accessible on {CurrentPlatform}");
    }

    [Fact(DisplayName = "PSP-007: Nested paths map correctly")]
    public void PSP007()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var deepPath = Path.Combine(Path.GetTempPath(), "level1", "level2", "level3", "file.txt");

        // Act
        var canonical = mapper.MapToCanonicalPath(deepPath);

        // Assert
        canonical.IsSuccess.Should().BeTrue();
        canonical.Value.Segments.Should().Contain("level1");
        canonical.Value.Segments.Should().Contain("level2");
        canonical.Value.Segments.Should().Contain("level3");
        canonical.Value.Segments.Should().Contain("file.txt");
    }

    [Fact(DisplayName = "PSP-008: File operations preserve content integrity across path mapping")]
    public async Task PSP008()
    {
        // Arrange
        var mapper = GetPlatformMapper();
        var fileSystem = new LocalFileSystem(mapper);

        var nativeFile = Path.Combine(Path.GetTempPath(), $"integrity_{System.Guid.NewGuid()}.bin");
        var content = new byte[1024];
        new System.Random(42).NextBytes(content);

        try
        {
            var canonicalPath = mapper.MapToCanonicalPath(nativeFile).Value;

            // Act - write binary content
            using (var writeStream = (await fileSystem.OpenWriteAsync(canonicalPath)).Value)
            {
                await writeStream.WriteAsync(content, 0, content.Length);
            }

            // Read back
            byte[] readContent;
            using (var readStream = (await fileSystem.OpenReadAsync(canonicalPath)).Value)
            {
                using var ms = new MemoryStream();
                await readStream.CopyToAsync(ms);
                readContent = ms.ToArray();
            }

            // Assert
            readContent.Should().BeEquivalentTo(content, "binary content should be preserved exactly");
        }
        finally
        {
            if (File.Exists(nativeFile)) File.Delete(nativeFile);
        }
    }
}

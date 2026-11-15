using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.InMemory;
using FluentAssertions;
using Xunit;

namespace PlatformTests.Local;

/// <summary>
/// Tests that verify the case sensitivity abstraction works correctly on all platforms.
/// These tests ensure users get consistent, predictable behavior regardless of OS.
/// </summary>
public class CaseSensitivityTests : PlatformTestBase
{
    [Fact(DisplayName = "PSC-001: PlatformDefault comparer detects current platform correctly")]
    public void PSC001()
    {
        // Arrange & Act
        var comparer = CanonicalPathComparer.PlatformDefault;

        // Assert - verify the comparer matches expected platform behavior
        if (IsWindows)
        {
            comparer.IsCaseSensitive.Should().BeFalse($"Windows should use case-insensitive comparison (detected: {CurrentPlatform})");
        }
        else
        {
            comparer.IsCaseSensitive.Should().BeTrue($"Unix systems should use case-sensitive comparison (detected: {CurrentPlatform})");
        }
    }

    [Fact(DisplayName = "PSC-002: InMemoryFileSystem uses platform-appropriate case handling")]
    public async Task PSC002()
    {
        // Arrange
        var fileSystem = new InMemoryFileSystem();
        var upperPath = CanonicalPath.Create("/Test/File.txt").Value;
        var lowerPath = CanonicalPath.Create("/test/file.txt").Value;

        await fileSystem.CreateDirectoryAsync(CanonicalPath.Create("/Test").Value);
        await fileSystem.WriteAllTextAsync(upperPath, "content");

        // Act
        var upperExists = await fileSystem.FileExistsAsync(upperPath);
        var lowerExists = await fileSystem.FileExistsAsync(lowerPath);

        // Assert - behavior should match platform default
        upperExists.Value.Should().BeTrue("file created with uppercase path should exist");

        if (IsWindows)
        {
            lowerExists.Value.Should().BeTrue($"on {CurrentPlatform}, lowercase path should find the same file");
        }
        else
        {
            lowerExists.Value.Should().BeFalse($"on {CurrentPlatform}, lowercase path should NOT find uppercase file");
        }
    }

    [Fact(DisplayName = "PSC-003: InMemoryFileSystem handles case-variant file creation correctly")]
    public async Task PSC003()
    {
        // Arrange
        var fileSystem = new InMemoryFileSystem();
        var upperPath = CanonicalPath.Create("/Test.txt").Value;
        var lowerPath = CanonicalPath.Create("/test.txt").Value;

        // Act - write with uppercase, then write with lowercase
        await fileSystem.WriteAllTextAsync(upperPath, "UPPER");
        await fileSystem.WriteAllTextAsync(lowerPath, "lower");

        // Assert - behavior depends on platform
        var upperContent = await fileSystem.ReadAllTextAsync(upperPath);

        if (IsWindows)
        {
            // On Windows: same file, second write overwrites
            upperContent.Value.Should().Be("lower", $"on {CurrentPlatform}, case-insensitive write should overwrite");
        }
        else
        {
            // On Unix: different files
            upperContent.Value.Should().Be("UPPER", $"on {CurrentPlatform}, case-sensitive means different files");
            var lowerContent = await fileSystem.ReadAllTextAsync(lowerPath);
            lowerContent.Value.Should().Be("lower");
        }
    }

    [Fact(DisplayName = "PSC-004: Explicit CaseSensitive comparer works consistently on all platforms")]
    public void PSC004()
    {
        // Arrange
        var comparer = CanonicalPathComparer.CaseSensitive;
        var path1 = CanonicalPath.Create("/Test/File.txt").Value;
        var path2 = CanonicalPath.Create("/test/file.txt").Value;

        // Act & Assert - always case-sensitive regardless of platform
        comparer.IsCaseSensitive.Should().BeTrue();
        comparer.Equals(path1, path2).Should().BeFalse($"CaseSensitive comparer should distinguish case on {CurrentPlatform}");
    }

    [Fact(DisplayName = "PSC-005: Explicit CaseInsensitive comparer works consistently on all platforms")]
    public void PSC005()
    {
        // Arrange
        var comparer = CanonicalPathComparer.CaseInsensitive;
        var path1 = CanonicalPath.Create("/Test/File.txt").Value;
        var path2 = CanonicalPath.Create("/test/file.txt").Value;

        // Act & Assert - always case-insensitive regardless of platform
        comparer.IsCaseSensitive.Should().BeFalse();
        comparer.Equals(path1, path2).Should().BeTrue($"CaseInsensitive comparer should ignore case on {CurrentPlatform}");
    }

    [Fact(DisplayName = "PSC-006: IsChildOf respects platform case sensitivity")]
    public void PSC006()
    {
        // Arrange
        var comparer = CanonicalPathComparer.PlatformDefault;
        var parent = CanonicalPath.Create("/Parent").Value;
        var childExact = CanonicalPath.Create("/Parent/Child").Value;
        var childDifferentCase = CanonicalPath.Create("/parent/child").Value;

        // Act & Assert
        comparer.IsChildOf(childExact, parent).Should().BeTrue("exact case match should always be a child");

        if (IsWindows)
        {
            comparer.IsChildOf(childDifferentCase, parent).Should().BeTrue($"on {CurrentPlatform}, case-different path should be a child");
        }
        else
        {
            comparer.IsChildOf(childDifferentCase, parent).Should().BeFalse($"on {CurrentPlatform}, case-different path should NOT be a child");
        }
    }

    [Fact(DisplayName = "PSC-007: InMemoryFileSystem with explicit comparer overrides platform default")]
    public async Task PSC007()
    {
        // Arrange - force case-insensitive on Unix or case-sensitive on Windows
        var comparer = IsWindows ? CanonicalPathComparer.CaseSensitive : CanonicalPathComparer.CaseInsensitive;
        var fileSystem = new InMemoryFileSystem(comparer);

        var upperPath = CanonicalPath.Create("/Test.txt").Value;
        var lowerPath = CanonicalPath.Create("/test.txt").Value;

        await fileSystem.WriteAllTextAsync(upperPath, "content");

        // Act
        var lowerExists = await fileSystem.FileExistsAsync(lowerPath);

        // Assert - behavior should be opposite of platform default
        if (IsWindows)
        {
            // Forced case-sensitive on Windows
            lowerExists.Value.Should().BeFalse("explicit CaseSensitive comparer should make paths distinct");
        }
        else
        {
            // Forced case-insensitive on Unix
            lowerExists.Value.Should().BeTrue("explicit CaseInsensitive comparer should make paths equal");
        }
    }

    [Fact(DisplayName = "PSC-008: Hash codes are consistent with equality")]
    public void PSC008()
    {
        // Arrange
        var comparer = CanonicalPathComparer.PlatformDefault;
        var path1 = CanonicalPath.Create("/Test/File.txt").Value;
        var path2 = CanonicalPath.Create("/test/file.txt").Value;

        // Act
        var areEqual = comparer.Equals(path1, path2);
        var hash1 = comparer.GetHashCode(path1);
        var hash2 = comparer.GetHashCode(path2);

        // Assert - if equal, hash codes must match
        if (areEqual)
        {
            hash1.Should().Be(hash2, "equal paths must have equal hash codes");
        }
        // Note: unequal paths may still have same hash (collision), so we don't assert the inverse
    }
}

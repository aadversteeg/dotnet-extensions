using System.Runtime.InteropServices;
using Xunit;

namespace PlatformTests.Local;

/// <summary>
/// Base class for platform-specific tests providing common platform detection utilities.
/// </summary>
public abstract class PlatformTestBase
{
    /// <summary>
    /// Returns true if running on Windows.
    /// </summary>
    protected static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// Returns true if running on Linux.
    /// </summary>
    protected static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    /// <summary>
    /// Returns true if running on macOS.
    /// </summary>
    protected static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>
    /// Returns true if running on any Unix-like system (Linux or macOS).
    /// </summary>
    protected static bool IsUnix => IsLinux || IsMacOS;

    /// <summary>
    /// Gets a description of the current platform.
    /// </summary>
    protected static string CurrentPlatform
    {
        get
        {
            if (IsWindows) return "Windows";
            if (IsLinux) return "Linux";
            if (IsMacOS) return "macOS";
            return "Unknown";
        }
    }

    /// <summary>
    /// Skips the test if not running on Windows.
    /// </summary>
    protected static void SkipIfNotWindows(string? reason = null) =>
        Skip.IfNot(IsWindows, reason ?? "This test only runs on Windows");

    /// <summary>
    /// Skips the test if not running on Linux.
    /// </summary>
    protected static void SkipIfNotLinux(string? reason = null) =>
        Skip.IfNot(IsLinux, reason ?? "This test only runs on Linux");

    /// <summary>
    /// Skips the test if not running on macOS.
    /// </summary>
    protected static void SkipIfNotMacOS(string? reason = null) =>
        Skip.IfNot(IsMacOS, reason ?? "This test only runs on macOS");

    /// <summary>
    /// Skips the test if not running on a Unix-like system.
    /// </summary>
    protected static void SkipIfNotUnix(string? reason = null) =>
        Skip.IfNot(IsUnix, reason ?? "This test only runs on Unix-like systems (Linux or macOS)");

    /// <summary>
    /// Skips the test if running on Windows.
    /// </summary>
    protected static void SkipIfWindows(string? reason = null) =>
        Skip.If(IsWindows, reason ?? "This test does not run on Windows");

    /// <summary>
    /// Skips the test if running on Linux.
    /// </summary>
    protected static void SkipIfLinux(string? reason = null) =>
        Skip.If(IsLinux, reason ?? "This test does not run on Linux");
}

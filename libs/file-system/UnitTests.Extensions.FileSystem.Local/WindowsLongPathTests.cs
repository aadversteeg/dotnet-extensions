using System;
using System.IO;
using System.Runtime.InteropServices;
using Ave.Extensions.FileSystem.Local;
using FluentAssertions;
using Xunit;

namespace UnitTests.Extensions.FileSystem.Local
{
    /// <summary>
    /// The string functions of the long path support are pure and run on every platform. Whether the prefix is
    /// used at all depends on the runtime: only on Windows on .NET Framework (run these with -p:UseNet48=true).
    /// </summary>
    public class WindowsLongPathTests
    {
        private static readonly bool IsNetFrameworkOnWindows =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.Ordinal);

        [Theory(DisplayName = "WLP-001: AddPrefix adds the prefix to a drive-absolute path")]
        [InlineData(@"C:\x", @"\\?\C:\x")]
        [InlineData(@"d:\a\b.txt", @"\\?\d:\a\b.txt")]
        [InlineData(@"C:\", @"\\?\C:\")]
        public void WLP001(string path, string expected)
        {
            WindowsLongPath.AddPrefix(path).Should().Be(expected);
        }

        [Theory(DisplayName = "WLP-002: AddPrefix leaves a path that is not drive-absolute unchanged")]
        [InlineData(@"\\?\C:\x")]
        [InlineData(@"\\.\C:\x")]
        [InlineData(@"\\.\NUL")]
        [InlineData(@"\\server\share\x")]
        [InlineData(@"\\?\UNC\server\share\x")]
        [InlineData(@"relative\x")]
        [InlineData(@"\rooted\x")]
        [InlineData(@"C:relative")]
        [InlineData(@"C:/x")]
        [InlineData("/home/user/x")]
        [InlineData("C:")]
        [InlineData("")]
        public void WLP002(string path)
        {
            WindowsLongPath.AddPrefix(path).Should().Be(path);
        }

        [Theory(DisplayName = "WLP-003: RemovePrefix removes the prefix from a drive-absolute path")]
        [InlineData(@"\\?\C:\x", @"C:\x")]
        [InlineData(@"\\?\d:\a\b.txt", @"d:\a\b.txt")]
        [InlineData(@"\\?\C:\", @"C:\")]
        public void WLP003(string path, string expected)
        {
            WindowsLongPath.RemovePrefix(path).Should().Be(expected);
        }

        [Theory(DisplayName = "WLP-004: RemovePrefix leaves any other path unchanged")]
        [InlineData(@"C:\x")]
        [InlineData(@"\\.\C:\x")]
        [InlineData(@"\\server\share\x")]
        [InlineData(@"\\?\UNC\server\share\x")]
        [InlineData(@"\\?\Volume{00000000-0000-0000-0000-000000000000}\x")]
        [InlineData(@"\\?\")]
        [InlineData(@"relative\x")]
        [InlineData("/home/user/x")]
        [InlineData("")]
        public void WLP004(string path)
        {
            WindowsLongPath.RemovePrefix(path).Should().Be(path);
        }

        [Fact(DisplayName = "WLP-005: RemovePrefix undoes AddPrefix")]
        public void WLP005()
        {
            var path = @"C:\" + string.Join(@"\", new string('a', 100), new string('b', 100), new string('c', 100), "f.txt");

            WindowsLongPath.RemovePrefix(WindowsLongPath.AddPrefix(path)).Should().Be(path);
        }

        [Fact(DisplayName = "WLP-006: RemovePrefixes removes every prefix of a drive-absolute path in a text")]
        public void WLP006()
        {
            var message = @"Could not move '\\?\C:\a\b.txt' to '\\?\D:\c'.";

            WindowsLongPath.RemovePrefixes(message).Should().Be(@"Could not move 'C:\a\b.txt' to 'D:\c'.");
        }

        [Theory(DisplayName = "WLP-007: RemovePrefixes leaves a text without such a prefix unchanged")]
        [InlineData(@"Could not find a part of the path 'C:\a'.")]
        [InlineData(@"Could not find '\\?\UNC\server\share\a'.")]
        [InlineData(@"Could not find '\\.\C:\a'.")]
        [InlineData("")]
        public void WLP007(string message)
        {
            WindowsLongPath.RemovePrefixes(message).Should().Be(message);
        }

        [Fact(DisplayName = "WLP-008: IsActive is true only on Windows on .NET Framework")]
        public void WLP008()
        {
            // The test host accepts prefixed paths, so on .NET Framework the probe must find that.
            WindowsLongPath.IsActive.Should().Be(IsNetFrameworkOnWindows);
        }

        [Fact(DisplayName = "WLP-009: ToSystemPath gives the prefixed full path when active and the path unchanged otherwise")]
        public void WLP009()
        {
            var path = Path.Combine(Path.GetTempPath(), "a", "..", "b.txt");

            var result = WindowsLongPath.ToSystemPath(path);

            if (IsNetFrameworkOnWindows)
                result.Should().Be(@"\\?\" + Path.GetFullPath(path)).And.NotContain("..");
            else
                result.Should().Be(path);
        }

        [Theory(DisplayName = "WLP-010: ToSystemPath leaves an invalid, network or device path unchanged")]
        [InlineData(@"C:\x\a<b")]
        [InlineData(@"C:\x\a.txt:stream")]
        [InlineData(@"\\server\share\x")]
        [InlineData(@"\\.\NUL")]
        [InlineData("")]
        public void WLP010(string path)
        {
            WindowsLongPath.ToSystemPath(path).Should().Be(path);
        }

        [Fact(DisplayName = "WLP-011: GetName gives the name of a folder, and the root itself for a prefixed drive root")]
        public void WLP011()
        {
            var folder = new DirectoryInfo(Path.GetTempPath());
            WindowsLongPath.GetName(folder).Should().Be(folder.Name);

            if (IsNetFrameworkOnWindows)
            {
                var root = Path.GetPathRoot(Path.GetTempPath())!;
                WindowsLongPath.GetName(new DirectoryInfo(WindowsLongPath.AddPrefix(root))).Should().Be(new DirectoryInfo(root).Name);
            }
        }
    }
}

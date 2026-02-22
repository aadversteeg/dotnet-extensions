using Ave.Extensions.FileSystem;
using FluentAssertions;

namespace UnitTests.Extensions.FileSystem.Abstractions
{
    public class SystemPathTests
    {
        [Fact(DisplayName = "SP-001: IsAbsolute with Unix path should return true")]
        public void SP001()
        {
            SystemPath.IsAbsolute("/home/user").Should().BeTrue();
        }

        [Fact(DisplayName = "SP-002: IsAbsolute with Windows drive backslash should return true")]
        public void SP002()
        {
            SystemPath.IsAbsolute(@"C:\Users").Should().BeTrue();
        }

        [Fact(DisplayName = "SP-003: IsAbsolute with Windows drive forward slash should return true")]
        public void SP003()
        {
            SystemPath.IsAbsolute("C:/Users").Should().BeTrue();
        }

        [Fact(DisplayName = "SP-004: IsAbsolute with UNC backslash path should return true")]
        public void SP004()
        {
            SystemPath.IsAbsolute(@"\\server\share").Should().BeTrue();
        }

        [Fact(DisplayName = "SP-005: IsAbsolute with UNC forward slash path should return true")]
        public void SP005()
        {
            SystemPath.IsAbsolute("//server/share").Should().BeTrue();
        }

        [Fact(DisplayName = "SP-006: IsAbsolute with relative path should return false")]
        public void SP006()
        {
            SystemPath.IsAbsolute("src/file.txt").Should().BeFalse();
        }

        [Fact(DisplayName = "SP-007: IsAbsolute with dot-relative path should return false")]
        public void SP007()
        {
            SystemPath.IsAbsolute("./file.txt").Should().BeFalse();
        }

        [Fact(DisplayName = "SP-008: IsAbsolute with parent-relative path should return false")]
        public void SP008()
        {
            SystemPath.IsAbsolute("../file.txt").Should().BeFalse();
        }

        [Fact(DisplayName = "SP-009: IsAbsolute with bare filename should return false")]
        public void SP009()
        {
            SystemPath.IsAbsolute("file.txt").Should().BeFalse();
        }

        [Fact(DisplayName = "SP-010: IsAbsolute with null should return false")]
        public void SP010()
        {
            SystemPath.IsAbsolute(null).Should().BeFalse();
        }

        [Fact(DisplayName = "SP-011: IsAbsolute with empty string should return false")]
        public void SP011()
        {
            SystemPath.IsAbsolute("").Should().BeFalse();
        }

        [Fact(DisplayName = "SP-012: IsRelative with relative path should return true")]
        public void SP012()
        {
            SystemPath.IsRelative("src/file.txt").Should().BeTrue();
        }

        [Fact(DisplayName = "SP-013: IsRelative with absolute Unix path should return false")]
        public void SP013()
        {
            SystemPath.IsRelative("/home/user").Should().BeFalse();
        }

        [Fact(DisplayName = "SP-014: IsRelative with null should return false")]
        public void SP014()
        {
            SystemPath.IsRelative(null).Should().BeFalse();
        }

        [Fact(DisplayName = "SP-015: IsRelative with empty string should return false")]
        public void SP015()
        {
            SystemPath.IsRelative("").Should().BeFalse();
        }
    }
}

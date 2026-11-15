using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local.PathMappers;
using FluentAssertions;
using Xunit;

namespace UnitTests.Extensions.FileSystem.Local.Mapping
{
    public class WindowsPathMapperTests
    {
        private readonly WindowsPathMapper _mapper = new();

        [Fact(DisplayName = "WPM-001: MapToSystemPath drive root returns drive with trailing backslash")]
        public void WPM001()
        {
            var path = CanonicalPath.Create("/d").Value;
            var result = _mapper.MapToSystemPath(path);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(@"D:\");
        }

        [Fact(DisplayName = "WPM-002: MapToSystemPath drive with subdirectory returns correct Windows path")]
        public void WPM002()
        {
            var path = CanonicalPath.Create("/d/agents").Value;
            var result = _mapper.MapToSystemPath(path);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(@"D:\agents");
        }

        [Fact(DisplayName = "WPM-003: MapToSystemPath drive with nested path returns correct Windows path")]
        public void WPM003()
        {
            var path = CanonicalPath.Create("/d/agents/flora").Value;
            var result = _mapper.MapToSystemPath(path);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(@"D:\agents\flora");
        }

        [Fact(DisplayName = "WPM-004: MapToSystemPath WSL drive root returns drive with trailing backslash")]
        public void WPM004()
        {
            var path = CanonicalPath.Create("/mnt/d").Value;
            var result = _mapper.MapToSystemPath(path);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(@"D:\");
        }

        [Fact(DisplayName = "WPM-005: MapToSystemPath WSL drive with subdirectory returns correct Windows path")]
        public void WPM005()
        {
            var path = CanonicalPath.Create("/mnt/d/agents").Value;
            var result = _mapper.MapToSystemPath(path);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(@"D:\agents");
        }

        [Fact(DisplayName = "WPM-006: MapToCanonicalPath Windows absolute path maps correctly")]
        public void WPM006()
        {
            var result = _mapper.MapToCanonicalPath(@"D:\$RECYCLE.BIN");
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/d/$RECYCLE.BIN");
        }

        [Fact(DisplayName = "WPM-007: MapToCanonicalPath drive root maps correctly")]
        public void WPM007()
        {
            var result = _mapper.MapToCanonicalPath(@"D:\");
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/d/");
        }

        [Fact(DisplayName = "WPM-008: MapToSystemPath then MapToCanonicalPath round-trips correctly for drive root")]
        public void WPM008()
        {
            var original = CanonicalPath.Create("/d").Value;
            var systemPathResult = _mapper.MapToSystemPath(original);
            systemPathResult.IsSuccess.Should().BeTrue();
            systemPathResult.Value.Should().Be(@"D:\");

            // Simulate what Directory.GetDirectories("D:\") returns
            var childSystemPath = @"D:\$RECYCLE.BIN";
            var canonicalResult = _mapper.MapToCanonicalPath(childSystemPath);
            canonicalResult.IsSuccess.Should().BeTrue();
            canonicalResult.Value.Value.Should().Be("/d/$RECYCLE.BIN");
        }

        [Fact(DisplayName = "WPM-009: MapToSystemPath c drive root returns C with trailing backslash")]
        public void WPM009()
        {
            var path = CanonicalPath.Create("/c").Value;
            var result = _mapper.MapToSystemPath(path);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(@"C:\");
        }
    }
}

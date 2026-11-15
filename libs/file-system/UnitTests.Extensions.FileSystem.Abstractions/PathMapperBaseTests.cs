using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem;
using Ave.Extensions.Functional;
using FluentAssertions;

namespace UnitTests.Extensions.FileSystem.Abstractions
{
    public class PathMapperBaseTests
    {
        private readonly TestPathMapper _mapper = new();

        [Fact(DisplayName = "PMB-001: MapToCanonicalPath with simple segment should append to base path")]
        public void PMB001()
        {
            var basePath = CanonicalPath.Create("/home/user").Value;

            var result = _mapper.MapToCanonicalPath("docs", basePath);

            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/home/user/docs");
        }

        [Fact(DisplayName = "PMB-002: MapToCanonicalPath with nested relative path should resolve all segments")]
        public void PMB002()
        {
            var basePath = CanonicalPath.Create("/home").Value;

            var result = _mapper.MapToCanonicalPath("src/main.cs", basePath);

            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/home/src/main.cs");
        }

        [Fact(DisplayName = "PMB-003: MapToCanonicalPath with dot segment should return base path unchanged")]
        public void PMB003()
        {
            var basePath = CanonicalPath.Create("/home/user").Value;

            var result = _mapper.MapToCanonicalPath(".", basePath);

            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/home/user");
        }

        [Fact(DisplayName = "PMB-004: MapToCanonicalPath with dot-slash prefix should resolve correctly")]
        public void PMB004()
        {
            var basePath = CanonicalPath.Create("/home").Value;

            var result = _mapper.MapToCanonicalPath("./file.txt", basePath);

            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/home/file.txt");
        }

        [Fact(DisplayName = "PMB-005: MapToCanonicalPath with parent segment should navigate up")]
        public void PMB005()
        {
            var basePath = CanonicalPath.Create("/home/user").Value;

            var result = _mapper.MapToCanonicalPath("..", basePath);

            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/home");
        }

        [Fact(DisplayName = "PMB-006: MapToCanonicalPath with parent then child should resolve to sibling")]
        public void PMB006()
        {
            var basePath = CanonicalPath.Create("/home/user").Value;

            var result = _mapper.MapToCanonicalPath("../sibling/file.txt", basePath);

            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/home/sibling/file.txt");
        }

        [Fact(DisplayName = "PMB-007: MapToCanonicalPath with multiple parent segments should navigate up correctly")]
        public void PMB007()
        {
            var basePath = CanonicalPath.Create("/a/b/c").Value;

            var result = _mapper.MapToCanonicalPath("../../file.txt", basePath);

            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/a/file.txt");
        }

        [Fact(DisplayName = "PMB-008: MapToCanonicalPath escaping root should return error")]
        public void PMB008()
        {
            var basePath = CanonicalPath.Create("/a/b").Value;

            var result = _mapper.MapToCanonicalPath("../../..", basePath);

            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.RelativePathEscapesRoot);
        }

        [Fact(DisplayName = "PMB-009: MapToCanonicalPath with null should return error")]
        public void PMB009()
        {
            var basePath = CanonicalPath.Create("/home").Value;

            var result = _mapper.MapToCanonicalPath(null!, basePath);

            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.RelativePathNullOrEmpty);
        }

        [Fact(DisplayName = "PMB-010: MapToCanonicalPath with empty string should return error")]
        public void PMB010()
        {
            var basePath = CanonicalPath.Create("/home").Value;

            var result = _mapper.MapToCanonicalPath("", basePath);

            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.RelativePathNullOrEmpty);
        }

        [Fact(DisplayName = "PMB-011: MapToCanonicalPath with backslash separator should resolve correctly")]
        public void PMB011()
        {
            var basePath = CanonicalPath.Create("/home").Value;

            var result = _mapper.MapToCanonicalPath("src\\main.cs", basePath);

            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/home/src/main.cs");
        }

        [Fact(DisplayName = "PMB-012: MapToCanonicalPath with mixed separators should resolve correctly")]
        public void PMB012()
        {
            var basePath = CanonicalPath.Create("/home").Value;

            var result = _mapper.MapToCanonicalPath("src/sub\\file.cs", basePath);

            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/home/src/sub/file.cs");
        }

        [Fact(DisplayName = "PMB-013: MapToCanonicalPath against root should resolve correctly")]
        public void PMB013()
        {
            var basePath = CanonicalPath.Root;

            var result = _mapper.MapToCanonicalPath("file.txt", basePath);

            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/file.txt");
        }

        [Fact(DisplayName = "PMB-014: MapToCanonicalPath with parent from root should return error")]
        public void PMB014()
        {
            var basePath = CanonicalPath.Root;

            var result = _mapper.MapToCanonicalPath("..", basePath);

            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.RelativePathEscapesRoot);
        }

        /// <summary>
        /// Minimal concrete implementation of PathMapperBase for testing the relative path resolution.
        /// </summary>
        private class TestPathMapper : PathMapperBase
        {
            public override Result<string, Error> MapToSystemPath(CanonicalPath canonicalPath)
            {
                return Result<string, Error>.Success(canonicalPath.Value);
            }

            public override Result<CanonicalPath, Error> MapToCanonicalPath(string systemPath)
            {
                return CanonicalPath.Create(systemPath);
            }
        }
    }
}

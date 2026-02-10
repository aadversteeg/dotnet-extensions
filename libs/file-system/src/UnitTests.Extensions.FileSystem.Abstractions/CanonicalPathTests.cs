using System;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.FileSystem;
using Ave.Extensions.Functional;
using FluentAssertions;

namespace UnitTests.Extensions.FileSystem.Abstractions
{
    public class CanonicalPathTests
    {
        [Fact(DisplayName = "CP-001: Create from valid root path returns success")]
        public void CP001()
        {
            // Act
            var result = CanonicalPath.Create("/");

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/");
            result.Value.Segments.Should().BeEmpty();
        }

        [Fact(DisplayName = "CP-002: Create from valid path with segments returns success")]
        public void CP002()
        {
            // Act
            var result = CanonicalPath.Create("/users/documents");

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/users/documents");
            result.Value.Segments.Should().Equal("users", "documents");
        }

        [Fact(DisplayName = "CP-003: Create from null path returns failure")]
        public void CP003()
        {
            // Act
            var result = CanonicalPath.Create((string)null!);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.PathNullOrEmpty);
        }

        [Fact(DisplayName = "CP-004: Create from empty path returns failure")]
        public void CP004()
        {
            // Act
            var result = CanonicalPath.Create("");

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.PathNullOrEmpty);
        }

        [Fact(DisplayName = "CP-005: Create from path not starting with slash returns failure")]
        public void CP005()
        {
            // Act
            var result = CanonicalPath.Create("users/documents");

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.PathMustStartWithSlash);
        }

        [Fact(DisplayName = "CP-006: Create from path containing backslashes returns failure")]
        public void CP006()
        {
            // Act
            var result = CanonicalPath.Create("/users\\documents");

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.PathContainsBackslashes);
        }

        [Fact(DisplayName = "CP-007: Create from path with consecutive slashes returns failure")]
        public void CP007()
        {
            // Act
            var result = CanonicalPath.Create("/users//documents");

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.PathContainsConsecutiveSlashes);
        }

        [Fact(DisplayName = "CP-008: Create from empty segments array returns root")]
        public void CP008()
        {
            // Act
            var result = CanonicalPath.Create(Array.Empty<string>());

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(CanonicalPath.Root);
        }

        [Fact(DisplayName = "CP-009: Create from valid segments returns success")]
        public void CP009()
        {
            // Act
            var result = CanonicalPath.Create(new[] { "users", "documents" });

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/users/documents");
            result.Value.Segments.Should().Equal("users", "documents");
        }

        [Fact(DisplayName = "CP-010: Create from segments with null segment returns failure")]
        public void CP010()
        {
            // Act
            var result = CanonicalPath.Create(new[] { "users", null!, "documents" });

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.SegmentNullOrEmpty);
            result.Error.Message.Should().Contain("index 1");
        }

        [Fact(DisplayName = "CP-011: Create from segments with empty segment returns failure")]
        public void CP011()
        {
            // Act
            var result = CanonicalPath.Create(new[] { "users", "", "documents" });

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.SegmentNullOrEmpty);
            result.Error.Message.Should().Contain("index 1");
        }

        [Fact(DisplayName = "CP-012: Create from segments with slash in segment returns failure")]
        public void CP012()
        {
            // Act
            var result = CanonicalPath.Create(new[] { "users", "do/cs", "file" });

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.SegmentContainsPathSeparators);
            result.Error.Message.Should().Contain("index 1");
        }

        [Fact(DisplayName = "CP-013: Root property returns root path")]
        public void CP013()
        {
            // Act & Assert
            CanonicalPath.Root.Value.Should().Be("/");
            CanonicalPath.Root.Segments.Should().BeEmpty();
        }

        [Fact(DisplayName = "CP-014: GetParent of root returns failure")]
        public void CP014()
        {
            // Act
            var result = CanonicalPath.Root.GetParent();

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.RootHasNoParent);
        }

        [Fact(DisplayName = "CP-015: GetParent of direct child returns root")]
        public void CP015()
        {
            // Arrange
            var path = CanonicalPath.Create("/users").Value;

            // Act
            var result = path.GetParent();

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(CanonicalPath.Root);
        }

        [Fact(DisplayName = "CP-016: GetParent of nested path returns correct parent")]
        public void CP016()
        {
            // Arrange
            var path = CanonicalPath.Create("/users/documents/file.txt").Value;

            // Act
            var result = path.GetParent();

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/users/documents");
            result.Value.Segments.Should().Equal("users", "documents");
        }

        [Fact(DisplayName = "CP-017: GetName of root returns slash")]
        public void CP017()
        {
            // Act
            var name = CanonicalPath.Root.GetName();

            // Assert
            name.Should().Be("/");
        }

        [Fact(DisplayName = "CP-018: GetName of file path returns filename")]
        public void CP018()
        {
            // Arrange
            var path = CanonicalPath.Create("/users/documents/file.txt").Value;

            // Act
            var name = path.GetName();

            // Assert
            name.Should().Be("file.txt");
        }

        [Fact(DisplayName = "CP-019: Append single segment returns success")]
        public void CP019()
        {
            // Arrange
            var path = CanonicalPath.Create("/users").Value;

            // Act
            var result = path.Append("documents");

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/users/documents");
            result.Value.Segments.Should().Equal("users", "documents");
        }

        [Fact(DisplayName = "CP-020: Append null segment returns failure")]
        public void CP020()
        {
            // Arrange
            var path = CanonicalPath.Create("/users").Value;

            // Act
            var result = path.Append((string)null!);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.AppendSegmentNullOrEmpty);
        }

        [Fact(DisplayName = "CP-021: Append empty segment returns failure")]
        public void CP021()
        {
            // Arrange
            var path = CanonicalPath.Create("/users").Value;

            // Act
            var result = path.Append("");

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.AppendSegmentNullOrEmpty);
        }

        [Fact(DisplayName = "CP-022: Append segment with slash returns failure")]
        public void CP022()
        {
            // Arrange
            var path = CanonicalPath.Create("/users").Value;

            // Act
            var result = path.Append("docs/file");

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.AppendSegmentContainsPathSeparators);
        }

        [Fact(DisplayName = "CP-023: Append segments array returns success")]
        public void CP023()
        {
            // Arrange
            var path = CanonicalPath.Create("/users").Value;

            // Act
            var result = path.Append(new[] { "documents", "projects" });

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/users/documents/projects");
            result.Value.Segments.Should().Equal("users", "documents", "projects");
        }

        [Fact(DisplayName = "CP-024: Append null segments array returns failure")]
        public void CP024()
        {
            // Arrange
            var path = CanonicalPath.Create("/users").Value;

            // Act
            var result = path.Append((string[])null!);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.AppendSegmentsNull);
        }

        [Fact(DisplayName = "CP-025: Append empty segments array returns failure")]
        public void CP025()
        {
            // Arrange
            var path = CanonicalPath.Create("/users").Value;

            // Act
            var result = path.Append(Array.Empty<string>());

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.AppendSegmentsEmpty);
        }

        [Fact(DisplayName = "CP-026: IsChildOf returns true for valid child")]
        public void CP026()
        {
            // Arrange
            var parent = CanonicalPath.Create("/users").Value;
            var child = CanonicalPath.Create("/users/documents/file.txt").Value;

            // Act
            var result = child.IsChildOf(parent);

            // Assert
            result.Should().BeTrue();
        }

        [Fact(DisplayName = "CP-027: IsChildOf returns false for non-child")]
        public void CP027()
        {
            // Arrange
            var path1 = CanonicalPath.Create("/users").Value;
            var path2 = CanonicalPath.Create("/home/documents").Value;

            // Act
            var result = path2.IsChildOf(path1);

            // Assert
            result.Should().BeFalse();
        }

        [Fact(DisplayName = "CP-028: IsChildOf returns false for same path")]
        public void CP028()
        {
            // Arrange
            var path = CanonicalPath.Create("/users/documents").Value;

            // Act
            var result = path.IsChildOf(path);

            // Assert
            result.Should().BeFalse();
        }

        [Fact(DisplayName = "CP-029: IsParentOf returns true for valid parent")]
        public void CP029()
        {
            // Arrange
            var parent = CanonicalPath.Create("/users").Value;
            var child = CanonicalPath.Create("/users/documents/file.txt").Value;

            // Act
            var result = parent.IsParentOf(child);

            // Assert
            result.Should().BeTrue();
        }

        [Fact(DisplayName = "CP-030: IsParentOf returns false for non-parent")]
        public void CP030()
        {
            // Arrange
            var path1 = CanonicalPath.Create("/users").Value;
            var path2 = CanonicalPath.Create("/home/documents").Value;

            // Act
            var result = path1.IsParentOf(path2);

            // Assert
            result.Should().BeFalse();
        }

        [Fact(DisplayName = "CP-031: Contains single segment returns correct index")]
        public void CP031()
        {
            // Arrange
            var path = CanonicalPath.Create("/users/documents/projects").Value;

            // Act
            var result = path.Contains("documents");

            // Assert
            result.Should().Be(1);
        }

        [Fact(DisplayName = "CP-032: Contains single segment returns -1 when not found")]
        public void CP032()
        {
            // Arrange
            var path = CanonicalPath.Create("/users/documents/projects").Value;

            // Act
            var result = path.Contains("missing");

            // Assert
            result.Should().Be(-1);
        }

        [Fact(DisplayName = "CP-033: Contains null segment returns -1")]
        public void CP033()
        {
            // Arrange
            var path = CanonicalPath.Create("/users/documents").Value;

            // Act
            var result = path.Contains((string)null!);

            // Assert
            result.Should().Be(-1);
        }

        [Fact(DisplayName = "CP-034: Contains segments sequence returns correct index")]
        public void CP034()
        {
            // Arrange
            var path = CanonicalPath.Create("/users/documents/projects/myapp").Value;

            // Act
            var result = path.Contains(new[] { "documents", "projects" });

            // Assert
            result.Should().Be(1);
        }

        [Fact(DisplayName = "CP-035: Contains segments sequence returns -1 when not found")]
        public void CP035()
        {
            // Arrange
            var path = CanonicalPath.Create("/users/documents/projects").Value;

            // Act
            var result = path.Contains(new[] { "documents", "missing" });

            // Assert
            result.Should().Be(-1);
        }

        [Fact(DisplayName = "CP-036: Contains null segments array returns -1")]
        public void CP036()
        {
            // Arrange
            var path = CanonicalPath.Create("/users/documents").Value;

            // Act
            var result = path.Contains((string[])null!);

            // Assert
            result.Should().Be(-1);
        }

        [Fact(DisplayName = "CP-037: Contains empty segments array returns -1")]
        public void CP037()
        {
            // Arrange
            var path = CanonicalPath.Create("/users/documents").Value;

            // Act
            var result = path.Contains(Array.Empty<string>());

            // Assert
            result.Should().Be(-1);
        }

        [Fact(DisplayName = "CP-038: Equals returns true for identical paths")]
        public void CP038()
        {
            // Arrange
            var path1 = CanonicalPath.Create("/users/DOCUMENTS").Value;
            var path2 = CanonicalPath.Create("/users/documents").Value;

            // Act & Assert
            path1.Equals(path2).Should().BeTrue();
            (path1 == path2).Should().BeTrue();
        }

        [Fact(DisplayName = "CP-039: Equals returns false for different paths")]
        public void CP039()
        {
            // Arrange
            var path1 = CanonicalPath.Create("/users/documents").Value;
            var path2 = CanonicalPath.Create("/users/projects").Value;

            // Act & Assert
            path1.Equals(path2).Should().BeFalse();
            (path1 == path2).Should().BeFalse();
        }

        [Fact(DisplayName = "CP-040: GetHashCode is consistent for equal paths")]
        public void CP040()
        {
            // Arrange
            var path1 = CanonicalPath.Create("/users/DOCUMENTS").Value;
            var path2 = CanonicalPath.Create("/users/documents").Value;

            // Act & Assert
            path1.GetHashCode().Should().Be(path2.GetHashCode());
        }

        [Fact(DisplayName = "CP-041: ToString returns path value")]
        public void CP041()
        {
            // Arrange
            var path = CanonicalPath.Create("/users/documents").Value;

            // Act
            var result = path.ToString();

            // Assert
            result.Should().Be("/users/documents");
        }

        [Fact(DisplayName = "CP-042: Implicit conversion to string works")]
        public void CP042()
        {
            // Arrange
            var path = CanonicalPath.Create("/users/documents").Value;

            // Act
            string result = path.Value;

            // Assert
            result.Should().Be("/users/documents");
        }
    }
}
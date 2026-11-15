using System;
using System.IO;
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

        [Fact(DisplayName = "CP-005: Create from relative path returns success")]
        public void CP005()
        {
            // Act
            var result = CanonicalPath.Create("users/documents");

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("users/documents");
            result.Value.Segments.Should().Equal("users", "documents");
            result.Value.IsRelative.Should().BeTrue();
            result.Value.IsAbsolute.Should().BeFalse();
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

        [Fact(DisplayName = "CP-043: IsAbsolute returns true for absolute path")]
        public void CP043()
        {
            // Arrange
            var path = CanonicalPath.Create("/users/documents").Value;

            // Act & Assert
            path.IsAbsolute.Should().BeTrue();
            path.IsRelative.Should().BeFalse();
        }

        [Fact(DisplayName = "CP-044: IsRelative returns true for relative path")]
        public void CP044()
        {
            // Arrange
            var path = CanonicalPath.Create("inbox").Value;

            // Act & Assert
            path.IsRelative.Should().BeTrue();
            path.IsAbsolute.Should().BeFalse();
        }

        [Fact(DisplayName = "CP-045: Create from dotted relative path returns success")]
        public void CP045()
        {
            // Act
            var result = CanonicalPath.Create("./src");

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("./src");
            result.Value.IsRelative.Should().BeTrue();
        }

        [Fact(DisplayName = "CP-046: Create from parent-relative path returns success")]
        public void CP046()
        {
            // Act
            var result = CanonicalPath.Create("../sibling");

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Segments.Should().Equal("..", "sibling");
        }

        [Fact(DisplayName = "CP-047: Create from relative path with dotfile returns success")]
        public void CP047()
        {
            // Act
            var result = CanonicalPath.Create(".flora/STATE.md");

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Segments.Should().Equal(".flora", "STATE.md");
        }

        [Fact(DisplayName = "CP-048: Resolve relative path against absolute base")]
        public void CP048()
        {
            // Arrange
            var relative = CanonicalPath.Create("inbox").Value;
            var basePath = CanonicalPath.Create("/c/project/tasks").Value;

            // Act
            var result = relative.Resolve(basePath);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/c/project/tasks/inbox");
            result.Value.IsAbsolute.Should().BeTrue();
        }

        [Fact(DisplayName = "CP-049: Resolve with dot segments normalizes correctly")]
        public void CP049()
        {
            // Arrange
            var relative = CanonicalPath.Create("./src/../lib").Value;
            var basePath = CanonicalPath.Create("/c/project").Value;

            // Act
            var result = relative.Resolve(basePath);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/c/project/lib");
        }

        [Fact(DisplayName = "CP-050: Resolve with parent beyond root returns failure")]
        public void CP050()
        {
            // Arrange
            var relative = CanonicalPath.Create("../../..").Value;
            var basePath = CanonicalPath.Create("/c/project").Value;

            // Act
            var result = relative.Resolve(basePath);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.RelativePathEscapesRoot);
        }

        [Fact(DisplayName = "CP-051: Resolve absolute path against base returns itself")]
        public void CP051()
        {
            // Arrange
            var absolute = CanonicalPath.Create("/c/other/path").Value;
            var basePath = CanonicalPath.Create("/c/project").Value;

            // Act
            var result = absolute.Resolve(basePath);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(absolute);
        }

        [Fact(DisplayName = "CP-052: Resolve with non-absolute base returns failure")]
        public void CP052()
        {
            // Arrange
            var relative = CanonicalPath.Create("inbox").Value;
            var relativeBase = CanonicalPath.Create("some/path").Value;

            // Act
            var result = relative.Resolve(relativeBase);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.ResolveRequiresAbsoluteBase);
        }

        [Fact(DisplayName = "CP-053: GetExtension returns dot extension for file")]
        public void CP053()
        {
            // Arrange
            var path = CanonicalPath.Create("/project/readme.md").Value;

            // Act & Assert
            path.GetExtension().Should().Be(".md");
        }

        [Fact(DisplayName = "CP-054: GetExtension returns empty for no extension")]
        public void CP054()
        {
            // Arrange
            var path = CanonicalPath.Create("/project/Makefile").Value;

            // Act & Assert
            path.GetExtension().Should().BeEmpty();
        }

        [Fact(DisplayName = "CP-055: GetExtension returns empty for hidden file without extension")]
        public void CP055()
        {
            // Arrange
            var path = CanonicalPath.Create("/project/.gitignore").Value;

            // Act & Assert
            path.GetExtension().Should().BeEmpty();
        }

        [Fact(DisplayName = "CP-056: GetExtension returns last extension for multiple dots")]
        public void CP056()
        {
            // Arrange
            var path = CanonicalPath.Create("/project/archive.tar.gz").Value;

            // Act & Assert
            path.GetExtension().Should().Be(".gz");
        }

        [Fact(DisplayName = "CP-057: GetExtension returns empty for root")]
        public void CP057()
        {
            // Act & Assert
            CanonicalPath.Root.GetExtension().Should().BeEmpty();
        }

        [Fact(DisplayName = "CP-058: GetNameWithoutExtension returns name without extension")]
        public void CP058()
        {
            // Arrange
            var path = CanonicalPath.Create("/project/readme.md").Value;

            // Act & Assert
            path.GetNameWithoutExtension().Should().Be("readme");
        }

        [Fact(DisplayName = "CP-059: GetNameWithoutExtension returns full name for no extension")]
        public void CP059()
        {
            // Arrange
            var path = CanonicalPath.Create("/project/Makefile").Value;

            // Act & Assert
            path.GetNameWithoutExtension().Should().Be("Makefile");
        }

        [Fact(DisplayName = "CP-060: GetNameWithoutExtension returns hidden file name")]
        public void CP060()
        {
            // Arrange
            var path = CanonicalPath.Create("/project/.gitignore").Value;

            // Act & Assert
            path.GetNameWithoutExtension().Should().Be(".gitignore");
        }

        [Fact(DisplayName = "CP-061: ChangeExtension changes file extension")]
        public void CP061()
        {
            // Arrange
            var path = CanonicalPath.Create("/project/readme.md").Value;

            // Act
            var result = path.ChangeExtension(".txt");

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/project/readme.txt");
        }

        [Fact(DisplayName = "CP-062: ChangeExtension removes extension with empty string")]
        public void CP062()
        {
            // Arrange
            var path = CanonicalPath.Create("/project/readme.md").Value;

            // Act
            var result = path.ChangeExtension("");

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/project/readme");
        }

        [Fact(DisplayName = "CP-063: ChangeExtension on root returns failure")]
        public void CP063()
        {
            // Act
            var result = CanonicalPath.Root.ChangeExtension(".txt");

            // Assert
            result.IsFailure.Should().BeTrue();
        }

        [Fact(DisplayName = "CP-064: Append on relative path preserves relative")]
        public void CP064()
        {
            // Arrange
            var path = CanonicalPath.Create("tasks").Value;

            // Act
            var result = path.Append("inbox");

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("tasks/inbox");
            result.Value.IsRelative.Should().BeTrue();
        }

        [Fact(DisplayName = "CP-065: GetParent on relative multi-segment path returns relative parent")]
        public void CP065()
        {
            // Arrange
            var path = CanonicalPath.Create("tasks/inbox/file.md").Value;

            // Act
            var result = path.GetParent();

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("tasks/inbox");
            result.Value.IsRelative.Should().BeTrue();
        }

        [Fact(DisplayName = "CP-066: GetParent on single-segment relative path returns failure")]
        public void CP066()
        {
            // Arrange
            var path = CanonicalPath.Create("inbox").Value;

            // Act
            var result = path.GetParent();

            // Assert
            result.IsFailure.Should().BeTrue();
        }

        [Fact(DisplayName = "CP-067: ChangeExtension on relative path preserves relative")]
        public void CP067()
        {
            // Arrange
            var path = CanonicalPath.Create("readme.md").Value;

            // Act
            var result = path.ChangeExtension(".txt");

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("readme.txt");
            result.Value.IsRelative.Should().BeTrue();
        }

        [Fact(DisplayName = "CP-068: Create from whitespace-only path returns failure")]
        public void CP068()
        {
            // Act
            var result = CanonicalPath.Create("   ");

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be(FileSystemErrorCodes.CanonicalPath.PathNullOrEmpty);
        }

        [Fact(DisplayName = "CP-069: GetExtension on relative path works")]
        public void CP069()
        {
            // Arrange
            var path = CanonicalPath.Create("docs/readme.md").Value;

            // Act & Assert
            path.GetExtension().Should().Be(".md");
        }

        [Fact(DisplayName = "CP-070: Resolve nested relative path")]
        public void CP070()
        {
            // Arrange
            var relative = CanonicalPath.Create(".flora/STATE.md").Value;
            var basePath = CanonicalPath.Create("/c/project").Value;

            // Act
            var result = relative.Resolve(basePath);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("/c/project/.flora/STATE.md");
        }

        [Fact(DisplayName = "CP-071: SanitizeFileName replaces invalid characters with default replacement")]
        public void CP071()
        {
            // Act
            var result = CanonicalPath.SanitizeFileName("my:file*name");

            // Assert
            result.Should().Be("my_file_name");
        }

        [Fact(DisplayName = "CP-072: SanitizeFileName returns empty string for empty input")]
        public void CP072()
        {
            // Act
            var result = CanonicalPath.SanitizeFileName("");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact(DisplayName = "CP-073: SanitizeFileName replaces all characters when all are invalid")]
        public void CP073()
        {
            // Arrange - build a string of only invalid chars
            var invalidChars = Path.GetInvalidFileNameChars();
            var input = new string(invalidChars);

            // Act
            var result = CanonicalPath.SanitizeFileName(input);

            // Assert
            result.Should().HaveLength(invalidChars.Length);
            result.Should().MatchRegex("^_+$");
        }

        [Fact(DisplayName = "CP-074: SanitizeFileName returns original string when no invalid characters")]
        public void CP074()
        {
            // Act
            var result = CanonicalPath.SanitizeFileName("valid-file_name.txt");

            // Assert
            result.Should().Be("valid-file_name.txt");
        }

        [Fact(DisplayName = "CP-075: SanitizeFileName uses custom replacement character")]
        public void CP075()
        {
            // Act
            var result = CanonicalPath.SanitizeFileName("my:file*name", '-');

            // Assert
            result.Should().Be("my-file-name");
        }

        [Fact(DisplayName = "CP-076: SanitizeFileName throws ArgumentNullException for null input")]
        public void CP076()
        {
            // Act
            var act = () => CanonicalPath.SanitizeFileName(null!);

            // Assert
            act.Should().Throw<ArgumentNullException>().WithParameterName("name");
        }

        [Fact(DisplayName = "CP-077: SanitizeFileName replaces the Windows invalid chars on every platform")]
        public void CP077()
        {
            // Arrange - the Windows set: control characters and " < > | : * ? \ /
            var input = "\"<>|:*?\\/\u0001\u001f";

            // Act
            var result = CanonicalPath.SanitizeFileName(input);

            // Assert
            result.Should().Be(new string('_', input.Length));
        }
    }
}
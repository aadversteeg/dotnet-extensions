using Ave.Extensions.FileSystem;
using FluentAssertions;

namespace UnitTests.Extensions.FileSystem.Abstractions
{
    public class FileSystemInfoTests
    {
        [Fact(DisplayName = "FSI-001: Constructor initializes all properties correctly")]
        public void FSI001()
        {
            // Arrange
            const bool isDurable = true;
            const bool supportsMonitoring = false;
            const bool isReadOnly = true;

            // Act
            var info = new FileSystemInfo(isDurable, supportsMonitoring, isReadOnly);

            // Assert
            info.IsDurable.Should().Be(isDurable);
            info.SupportsMonitoring.Should().Be(supportsMonitoring);
            info.IsReadOnly.Should().Be(isReadOnly);
        }

        [Fact(DisplayName = "FSI-002: Record equality works correctly")]
        public void FSI002()
        {
            // Arrange
            var info1 = new FileSystemInfo(true, false, true);
            var info2 = new FileSystemInfo(true, false, true);
            var info3 = new FileSystemInfo(false, true, false);

            // Act & Assert
            info1.Should().Be(info2);
            info1.Should().NotBe(info3);
            info1.Equals(info2).Should().BeTrue();
            info1.Equals(info3).Should().BeFalse();
        }

        [Fact(DisplayName = "FSI-003: GetHashCode returns consistent values for equal instances")]
        public void FSI003()
        {
            // Arrange
            var info1 = new FileSystemInfo(true, false, true);
            var info2 = new FileSystemInfo(true, false, true);

            // Act
            var hash1 = info1.GetHashCode();
            var hash2 = info2.GetHashCode();

            // Assert
            hash1.Should().Be(hash2);
        }

        [Fact(DisplayName = "FSI-004: ToString returns meaningful representation")]
        public void FSI004()
        {
            // Arrange
            var info = new FileSystemInfo(true, false, true);

            // Act
            var stringRepresentation = info.ToString();

            // Assert
            stringRepresentation.Should().NotBeNullOrEmpty();
            stringRepresentation.Should().Contain("IsDurable");
            stringRepresentation.Should().Contain("SupportsMonitoring");
            stringRepresentation.Should().Contain("IsReadOnly");
        }

        [Theory(DisplayName = "FSI-005: All combinations of boolean values work correctly")]
        [InlineData(true, true, true)]
        [InlineData(true, true, false)]
        [InlineData(true, false, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, true)]
        [InlineData(false, true, false)]
        [InlineData(false, false, true)]
        [InlineData(false, false, false)]
        public void FSI005(bool isDurable, bool supportsMonitoring, bool isReadOnly)
        {
            // Act
            var info = new FileSystemInfo(isDurable, supportsMonitoring, isReadOnly);

            // Assert
            info.IsDurable.Should().Be(isDurable);
            info.SupportsMonitoring.Should().Be(supportsMonitoring);
            info.IsReadOnly.Should().Be(isReadOnly);
        }
    }
}
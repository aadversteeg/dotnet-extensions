using System;
using Ave.Extensions.FileSystem.Traversal;
using FluentAssertions;

namespace UnitTests.Extensions.FileSystem.Abstractions.Traversal
{
    public class TraversalSummaryTests
    {
        [Fact(DisplayName = "TS-001: Constructor sets all properties correctly")]
        public void TS001()
        {
            // Arrange
            var filesProcessed = 10;
            var directoriesProcessed = 5;
            var totalBytesProcessed = 1024;
            var duration = TimeSpan.FromSeconds(30);
            var wasCancelled = true;

            // Act
            var summary = new TraversalSummary(
                filesProcessed,
                directoriesProcessed,
                totalBytesProcessed,
                duration,
                wasCancelled);

            // Assert
            summary.FilesProcessed.Should().Be(filesProcessed);
            summary.DirectoriesProcessed.Should().Be(directoriesProcessed);
            summary.TotalBytesProcessed.Should().Be(totalBytesProcessed);
            summary.Duration.Should().Be(duration);
            summary.WasCancelled.Should().Be(wasCancelled);
        }

        [Fact(DisplayName = "TS-002: Constructor accepts zero values")]
        public void TS002()
        {
            // Arrange & Act
            var summary = new TraversalSummary(0, 0, 0, TimeSpan.Zero, false);

            // Assert
            summary.FilesProcessed.Should().Be(0);
            summary.DirectoriesProcessed.Should().Be(0);
            summary.TotalBytesProcessed.Should().Be(0);
            summary.Duration.Should().Be(TimeSpan.Zero);
            summary.WasCancelled.Should().BeFalse();
        }

        [Fact(DisplayName = "TS-003: Constructor handles large values")]
        public void TS003()
        {
            // Arrange
            var largeNumber = long.MaxValue;
            var longDuration = TimeSpan.FromDays(365);

            // Act
            var summary = new TraversalSummary(
                largeNumber,
                largeNumber,
                largeNumber,
                longDuration,
                false);

            // Assert
            summary.FilesProcessed.Should().Be(largeNumber);
            summary.DirectoriesProcessed.Should().Be(largeNumber);
            summary.TotalBytesProcessed.Should().Be(largeNumber);
            summary.Duration.Should().Be(longDuration);
        }

        [Fact(DisplayName = "TS-004: Empty property returns default values")]
        public void TS004()
        {
            // Act
            var summary = TraversalSummary.Empty;

            // Assert
            summary.FilesProcessed.Should().Be(0);
            summary.DirectoriesProcessed.Should().Be(0);
            summary.TotalBytesProcessed.Should().Be(0);
            summary.Duration.Should().Be(TimeSpan.Zero);
            summary.WasCancelled.Should().BeFalse();
        }

        [Fact(DisplayName = "TS-005: Cancelled factory method sets cancellation flag")]
        public void TS005()
        {
            // Arrange
            var filesProcessed = 10;
            var directoriesProcessed = 5;
            var duration = TimeSpan.FromSeconds(30);

            // Act
            var summary = TraversalSummary.Cancelled(filesProcessed, directoriesProcessed, duration);

            // Assert
            summary.FilesProcessed.Should().Be(filesProcessed);
            summary.DirectoriesProcessed.Should().Be(directoriesProcessed);
            summary.TotalBytesProcessed.Should().Be(0); // Cancelled doesn't track bytes
            summary.Duration.Should().Be(duration);
            summary.WasCancelled.Should().BeTrue();
        }

        [Fact(DisplayName = "TS-006: Objects with same values are equal")]
        public void TS006()
        {
            // Arrange
            var summary1 = new TraversalSummary(10, 5, 1024, TimeSpan.FromSeconds(30), true);
            var summary2 = new TraversalSummary(10, 5, 1024, TimeSpan.FromSeconds(30), true);

            // Act & Assert
            summary1.Should().Be(summary2);
            summary1.GetHashCode().Should().Be(summary2.GetHashCode());
        }

        [Fact(DisplayName = "TS-007: Objects with different values are not equal")]
        public void TS007()
        {
            // Arrange
            var summary1 = new TraversalSummary(10, 5, 1024, TimeSpan.FromSeconds(30), true);
            var summary2 = new TraversalSummary(11, 5, 1024, TimeSpan.FromSeconds(30), true); // Different files count

            // Act & Assert
            summary1.Should().NotBe(summary2);
        }

        [Fact(DisplayName = "TS-008: ToString provides meaningful representation")]
        public void TS008()
        {
            // Arrange
            var summary = new TraversalSummary(
                filesProcessed: 10,
                directoriesProcessed: 5,
                totalBytesProcessed: 1024,
                duration: TimeSpan.FromMilliseconds(1500),
                wasCancelled: false);

            // Act
            var stringRepresentation = summary.ToString();

            // Assert
            stringRepresentation.Should().NotBeNullOrEmpty();
            stringRepresentation.Should().Contain("10"); // Files processed
            stringRepresentation.Should().Contain("5");  // Directories processed
        }
    }
}
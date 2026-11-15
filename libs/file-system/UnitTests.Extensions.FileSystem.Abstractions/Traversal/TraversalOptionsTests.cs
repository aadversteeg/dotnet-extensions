using Ave.Extensions.FileSystem.Traversal;
using FluentAssertions;

namespace UnitTests.Extensions.FileSystem.Abstractions.Traversal
{
    public class TraversalOptionsTests
    {
        [Fact(DisplayName = "TO-001: Default constructor creates options with default values")]
        public void TO001()
        {
            // Arrange & Act
            var options = new TraversalOptions();

            // Assert
            options.Recursive.Should().BeTrue();
            options.StrategyHint.Should().Be(TraversalStrategyHint.Auto);
            options.MaxDepth.Should().BeNull();
        }

        [Fact(DisplayName = "TO-002: Parameterized constructor sets all properties correctly")]
        public void TO002()
        {
            // Arrange & Act
            var options = new TraversalOptions(
                maxDepth: 5,
                recursive: true,
                strategyHint: TraversalStrategyHint.MemoryEfficient);

            // Assert
            options.Recursive.Should().BeTrue();
            options.StrategyHint.Should().Be(TraversalStrategyHint.MemoryEfficient);
            options.MaxDepth.Should().Be(5);
        }

        [Fact(DisplayName = "TO-003: WithRecursive method creates new instance with updated recursive value")]
        public void TO003()
        {
            // Arrange
            var original = new TraversalOptions(recursive: false);

            // Act
            var updated = original.WithRecursive(true);

            // Assert
            original.Recursive.Should().BeFalse(); // Original unchanged
            updated.Recursive.Should().BeTrue();   // New instance updated
            updated.StrategyHint.Should().Be(original.StrategyHint); // Other properties preserved
        }

        [Fact(DisplayName = "TO-007: WithStrategyHint method creates new instance with updated strategy hint")]
        public void TO007()
        {
            // Arrange
            var original = new TraversalOptions();

            // Act
            var updated = original.WithStrategyHint(TraversalStrategyHint.Parallel);

            // Assert
            original.StrategyHint.Should().Be(TraversalStrategyHint.Auto); // Original unchanged
            updated.StrategyHint.Should().Be(TraversalStrategyHint.Parallel); // New instance updated
        }

        [Fact(DisplayName = "TO-008: WithMaxDepth method creates new instance with updated max depth")]
        public void TO008()
        {
            // Arrange
            var original = new TraversalOptions();

            // Act
            var updated = original.WithMaxDepth(10);

            // Assert
            original.MaxDepth.Should().BeNull(); // Original unchanged
            updated.MaxDepth.Should().Be(10); // New instance updated
        }

        [Fact(DisplayName = "TO-009: Multiple With calls can be chained")]
        public void TO009()
        {
            // Arrange
            var original = new TraversalOptions();

            // Act
            var updated = original
                .WithRecursive(false)
                .WithMaxDepth(3);

            // Assert
            updated.Recursive.Should().BeFalse();
            updated.MaxDepth.Should().Be(3);
            // Unchanged properties should have default values
            updated.StrategyHint.Should().Be(TraversalStrategyHint.Auto);
        }

        [Fact(DisplayName = "TO-012: TopLevelOnly static property creates correct options")]
        public void TO012()
        {
            // Act
            var options = TraversalOptions.TopLevelOnly;

            // Assert
            options.Recursive.Should().BeFalse();
            options.StrategyHint.Should().Be(TraversalStrategyHint.Auto); // Default
        }
    }
}
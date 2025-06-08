using System;
using System.CommandLine;
using AutoFixture;
using FileSystemSamples.Common.Options;
using FluentAssertions;
using Xunit;

namespace FileSystemSamples.Common.Tests.Options;

/// <summary>
/// Unit tests for the OptionsBinder class.
/// </summary>
public class OptionsBinderTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public void OB001()
    {
        // Arrange
        var binder = new OptionsBinder<TestOptions>();
        var options = new[]
        {
            new Option<string>("--test-string", "Test string option"),
            new Option<int>("--test-int", "Test int option"),
            new Option<bool>("--test-bool", "Test bool option")
        };

        // Act & Assert
        var action = () => binder.Bind(options);
        action.Should().NotThrow("OptionsBinder should handle valid options");
    }

    [Fact]
    public void OB002()
    {
        // Arrange
        var binder = new OptionsBinder<TestOptions>();

        // Act & Assert
        var action = () => binder.Bind(null!);
        action.Should().Throw<ArgumentNullException>("OptionsBinder should validate input");
    }

    [Fact]
    public void OB003()
    {
        // Arrange
        var binder = new OptionsBinder<TestOptions>();
        var emptyOptions = Array.Empty<Option>();

        // Act
        var result = binder.Bind(emptyOptions);

        // Assert
        result.Should().NotBeNull("OptionsBinder should return instance even with no options");
    }

    private class TestOptions : ICommonOptions
    {
        public string Path { get; set; } = "";
        public Microsoft.Extensions.Logging.LogLevel LogLevel { get; set; }
        public bool Verbose { get; set; }
        public string? OutputPath { get; set; }
        public ExportFormat ExportFormat { get; set; }
        public Ave.Extensions.FileSystem.Traversal.TraversalStrategyHint Strategy { get; set; }
        public int? MaxDepth { get; set; }
        public string TestString { get; set; } = "";
        public int TestInt { get; set; }
        public bool TestBool { get; set; }
    }
}
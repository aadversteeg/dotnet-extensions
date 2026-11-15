using System;
using System.Threading.Tasks;
using AutoFixture;
using FileSystemSamples.Common.Export;
using FileSystemSamples.Common.Export.Formatters;
using FileSystemSamples.Common.Options;
using FluentAssertions;
using Moq;
using Xunit;

namespace FileSystemSamples.Common.Tests.Export;

/// <summary>
/// Unit tests for the ExporterFactory class.
/// </summary>
public class ExporterFactoryTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public void EF001()
    {
        // Arrange
        var mockFormatter = new Mock<IFormatter<string>>();
        mockFormatter.Setup(f => f.Format(It.IsAny<string>())).Returns("formatted");
        mockFormatter.Setup(f => f.FileExtension).Returns(".txt");
        mockFormatter.Setup(f => f.ContentType).Returns("text/plain");

        var factory = new ExporterFactory<string>(format => mockFormatter.Object);

        // Act & Assert
        var action = () => factory.ExportAsync("test", ExportFormat.Text);
        action.Should().NotThrow("ExporterFactory should handle valid formatters");
    }

    [Fact]
    public void EF002()
    {
        // Arrange
        var factory = new ExporterFactory<string>(format => throw new NotSupportedException());

        // Act & Assert
        var action = async () => await factory.ExportAsync("test", ExportFormat.Text);
        action.Should().ThrowAsync<NotSupportedException>("ExporterFactory should propagate formatter exceptions");
    }

    [Fact]
    public async Task EF003()
    {
        // Arrange
        var mockFormatter = new Mock<IFormatter<string>>();
        mockFormatter.Setup(f => f.Format(It.IsAny<string>())).Returns("formatted content");
        mockFormatter.Setup(f => f.FileExtension).Returns(".txt");
        mockFormatter.Setup(f => f.ContentType).Returns("text/plain");

        var factory = new ExporterFactory<string>(format => mockFormatter.Object);

        // Act
        var result = await factory.ExportAsync("test data", ExportFormat.Text);

        // Assert
        result.Should().Be("formatted content", "ExporterFactory should return formatted content when no output path");
        mockFormatter.Verify(f => f.Format("test data"), Times.Once);
    }

    [Fact]
    public async Task EF004()
    {
        // Arrange
        var tempFile = System.IO.Path.GetTempFileName();
        try
        {
            var mockFormatter = new Mock<IFormatter<string>>();
            mockFormatter.Setup(f => f.Format(It.IsAny<string>())).Returns("file content");
            mockFormatter.Setup(f => f.FileExtension).Returns(".txt");
            mockFormatter.Setup(f => f.ContentType).Returns("text/plain");

            var factory = new ExporterFactory<string>(format => mockFormatter.Object);

            // Act
            var result = await factory.ExportAsync("test data", ExportFormat.Text, tempFile);

            // Assert
            result.Should().BeNull("ExporterFactory should return null when writing to file");
            System.IO.File.ReadAllText(tempFile).Should().Be("file content");
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
                System.IO.File.Delete(tempFile);
        }
    }
}
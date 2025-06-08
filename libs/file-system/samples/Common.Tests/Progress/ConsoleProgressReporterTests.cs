using System;
using System.IO;
using AutoFixture;
using FileSystemSamples.Common.Progress;
using FluentAssertions;
using Xunit;

namespace FileSystemSamples.Common.Tests.Progress;

/// <summary>
/// Unit tests for the ConsoleProgressReporter class.
/// </summary>
public class ConsoleProgressReporterTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public void CPR001()
    {
        // Arrange
        using var stringWriter = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(stringWriter);

        try
        {
            var reporter = new ConsoleProgressReporter();
            var progress = new ProgressInfo(10, 5, 1024, TimeSpan.FromSeconds(1));

            // Act
            reporter.Report(progress);

            // Assert
            var output = stringWriter.ToString();
            output.Should().Contain("10", "Should show file count");
            output.Should().Contain("5", "Should show directory count");
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void CPR002()
    {
        // Arrange
        using var stringWriter = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(stringWriter);

        try
        {
            var reporter = new ConsoleProgressReporter();
            var completion = new CompletionInfo(100, 50, 2048, TimeSpan.FromMinutes(1), 2, false);

            // Act
            reporter.ReportComplete(completion);

            // Assert
            var output = stringWriter.ToString();
            output.Should().Contain("completed", StringComparison.OrdinalIgnoreCase);
            output.Should().Contain("100", "Should show final file count");
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void CPR003()
    {
        // Arrange
        using var stringWriter = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(stringWriter);

        try
        {
            var reporter = new ConsoleProgressReporter();
            var progressWithErrors = new ProgressInfo(10, 5, 1024, TimeSpan.FromSeconds(1), 
                errorCount: 3, currentPath: "/test/path");

            // Act
            reporter.Report(progressWithErrors);

            // Assert
            var output = stringWriter.ToString();
            output.Should().Contain("3", "Should show error count when errors present");
            output.Should().Contain("/test/path", "Should show current path");
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void CPR004()
    {
        // Arrange
        using var stringWriter = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(stringWriter);

        try
        {
            var reporter = new ConsoleProgressReporter();
            var cancelledCompletion = new CompletionInfo(50, 25, 1024, TimeSpan.FromSeconds(30), 0, true);

            // Act
            reporter.ReportComplete(cancelledCompletion);

            // Assert
            var output = stringWriter.ToString();
            output.Should().Contain("cancelled", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}
using System;
using Ave.Extensions.FileSystem;
using FluentAssertions;
using Xunit;

namespace UnitTests.Extensions.FileSystem.Local
{
    /// <summary>
    /// Unit tests for LocalFileSystem that focus on unique scenarios not covered by integration tests.
    /// Most LocalFileSystem functionality is comprehensively tested in the integration test suite.
    /// </summary>
    public class LocalFileSystemTests
    {
        [Fact(DisplayName = "LFS-001: CanonicalPath creation validates path format")]
        public void LFS001()
        {
            // Arrange
            var invalidPath = "";
            
            // Act
            // Test CanonicalPath validation logic, which is a prerequisite for LocalFileSystem operations
            var canonicalPathResult = CanonicalPath.Create(invalidPath);
            
            // Assert
            canonicalPathResult.IsFailure.Should().BeTrue();
            canonicalPathResult.Error.Code.Should().Be(ErrorCodes.CanonicalPath.PathNullOrEmpty);
        }

        [Fact(DisplayName = "LFS-002: Info property returns correct characteristics for LocalFileSystem")]
        public void LFS002()
        {
            // Arrange
            var fileSystem = new Ave.Extensions.FileSystem.Local.LocalFileSystem();

            // Act
            var info = fileSystem.Info;

            // Assert
            info.IsDurable.Should().BeTrue("local file system persists data across application restarts");
            info.SupportsMonitoring.Should().BeTrue("local file system supports FileSystemWatcher for monitoring");
            info.IsReadOnly.Should().BeFalse("local file system supports both read and write operations");
        }
    }
}
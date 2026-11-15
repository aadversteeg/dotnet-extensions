using System;
using System.Collections.Generic;
using System.IO;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.MimeDetective.Models;
using Ave.Extensions.FileSystem.Models;
using FluentAssertions;
using Xunit;

namespace UnitTests.Extensions.FileSystem.MimeDetective.Models
{
    public class FileSystemEntryWithContentTypeTests
    {
        [Fact(DisplayName = "FSWCT-001: Constructor sets all properties correctly")]
        public void FSWCT001()
        {
            // Arrange
            var path = CanonicalPath.Create("/test/file.pdf").Value;
            var createdUtc = DateTime.UtcNow.AddDays(-1);
            var modifiedUtc = DateTime.UtcNow;
            var lastAccessUtc = DateTime.UtcNow.AddHours(-1);
            var attributes = FileAttributes.Normal;
            var contentType = "application/pdf";
            var description = "PDF Document";
            var extensions = new[] { ".pdf", ".PDF" };
            var confidence = 0.95;

            // Act
            var entry = new FileSystemEntryWithContentType(
                path,
                false,
                12345L,
                ".pdf",
                createdUtc,
                modifiedUtc,
                lastAccessUtc,
                attributes,
                contentType,
                description,
                extensions,
                confidence);

            // Assert
            entry.Path.Should().Be(path);
            entry.IsDirectory.Should().BeFalse();
            entry.Size.Should().Be(12345L);
            entry.Extension.Should().Be(".pdf");
            entry.CreatedUtc.Should().Be(createdUtc);
            entry.ModifiedUtc.Should().Be(modifiedUtc);
            entry.LastAccessUtc.Should().Be(lastAccessUtc);
            entry.Attributes.Should().Be(attributes);
            entry.ContentType.Should().Be(contentType);
            entry.FileTypeDescription.Should().Be(description);
            entry.AlternativeExtensions.Should().BeEquivalentTo(extensions);
            entry.Confidence.Should().Be(confidence);
        }

        [Fact(DisplayName = "FSWCT-002: Name property returns filename from path")]
        public void FSWCT002()
        {
            // Arrange
            var path = CanonicalPath.Create("/documents/report.docx").Value;
            var entry = new FileSystemEntryWithContentType(
                path, false, 1000L, ".docx", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Normal);

            // Act & Assert
            entry.Name.Should().Be("report.docx");
        }

        [Fact(DisplayName = "FSWCT-003: FromEntry creates entry with content type information")]
        public void FSWCT003()
        {
            // Arrange
            var baseEntry = new FileSystemEntry(
                CanonicalPath.Create("/test/image.jpg").Value,
                false,
                5000L,
                ".jpg",
                DateTime.UtcNow.AddDays(-1),
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(-1),
                FileAttributes.Normal);

            var contentType = "image/jpeg";
            var description = "JPEG Image";
            var alternativeExtensions = new[] { ".jpg", ".jpeg" };

            // Act
            var extendedEntry = FileSystemEntryWithContentType.FromEntry(
                baseEntry,
                contentType,
                description,
                alternativeExtensions,
                0.9);

            // Assert
            extendedEntry.Path.Should().Be(baseEntry.Path);
            extendedEntry.Size.Should().Be(baseEntry.Size);
            extendedEntry.Extension.Should().Be(baseEntry.Extension);
            extendedEntry.ContentType.Should().Be(contentType);
            extendedEntry.FileTypeDescription.Should().Be(description);
            extendedEntry.AlternativeExtensions.Should().BeEquivalentTo(alternativeExtensions);
            extendedEntry.Confidence.Should().Be(0.9);
        }

        [Fact(DisplayName = "FSWCT-004: Directory entry has null content type properties")]
        public void FSWCT004()
        {
            // Arrange & Act
            var entry = new FileSystemEntryWithContentType(
                CanonicalPath.Create("/documents").Value,
                true, // isDirectory
                null, // size
                null, // extension
                DateTime.UtcNow,
                DateTime.UtcNow,
                DateTime.UtcNow,
                FileAttributes.Directory);

            // Assert
            entry.IsDirectory.Should().BeTrue();
            entry.Size.Should().BeNull();
            entry.Extension.Should().BeNull();
            entry.ContentType.Should().BeNull();
            entry.FileTypeDescription.Should().BeNull();
            entry.AlternativeExtensions.Should().BeEmpty();
            entry.Confidence.Should().Be(0.0);
        }

        [Fact(DisplayName = "FSWCT-005: IsHidden property reflects file attributes")]
        public void FSWCT005()
        {
            // Arrange & Act
            var visibleEntry = new FileSystemEntryWithContentType(
                CanonicalPath.Create("/test.txt").Value,
                false, 100L, ".txt", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Normal);

            var hiddenEntry = new FileSystemEntryWithContentType(
                CanonicalPath.Create("/hidden.txt").Value,
                false, 100L, ".txt", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Hidden);

            // Assert
            visibleEntry.IsHidden.Should().BeFalse();
            hiddenEntry.IsHidden.Should().BeTrue();
        }

        [Fact(DisplayName = "FSWCT-006: Empty collections are provided when null")]
        public void FSWCT006()
        {
            // Arrange & Act
            var entry = new FileSystemEntryWithContentType(
                CanonicalPath.Create("/test.txt").Value,
                false, 100L, ".txt", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Normal,
                alternativeExtensions: null);

            // Assert
            entry.AlternativeExtensions.Should().NotBeNull().And.BeEmpty();
        }
    }
}
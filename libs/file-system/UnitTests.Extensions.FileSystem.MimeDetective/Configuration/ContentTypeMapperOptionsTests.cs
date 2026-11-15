using Ave.Extensions.FileSystem.MimeDetective.Configuration;
using FluentAssertions;
using Xunit;

namespace UnitTests.Extensions.FileSystem.MimeDetective.Configuration
{
    public class ContentTypeMapperOptionsTests
    {
        [Fact(DisplayName = "CTMO-001: Default values are set correctly")]
        public void CTMO001()
        {
            // Arrange & Act
            var options = new ContentTypeMapperOptions();

            // Assert
            options.MaxBytesToRead.Should().Be(8192);
            options.IncludeAlternativeExtensions.Should().BeTrue();
            options.IncludeCategories.Should().BeTrue();
            options.FallbackToExtension.Should().BeTrue();
            options.DefinitionPack.Should().Be(DefinitionPack.Default);
            options.DefaultContentType.Should().Be("application/octet-stream");
        }

        [Fact(DisplayName = "CTMO-002: Properties can be modified")]
        public void CTMO002()
        {
            // Arrange
            var options = new ContentTypeMapperOptions();

            // Act
            options.MaxBytesToRead = 4096;
            options.IncludeAlternativeExtensions = false;
            options.IncludeCategories = false;
            options.FallbackToExtension = false;
            options.DefinitionPack = DefinitionPack.Exhaustive;
            options.DefaultContentType = "text/plain";

            // Assert
            options.MaxBytesToRead.Should().Be(4096);
            options.IncludeAlternativeExtensions.Should().BeFalse();
            options.IncludeCategories.Should().BeFalse();
            options.FallbackToExtension.Should().BeFalse();
            options.DefinitionPack.Should().Be(DefinitionPack.Exhaustive);
            options.DefaultContentType.Should().Be("text/plain");
        }

        [Theory(DisplayName = "CTMO-003: DefinitionPack enum has expected values")]
        [InlineData(DefinitionPack.Default)]
        [InlineData(DefinitionPack.Condensed)]
        [InlineData(DefinitionPack.Exhaustive)]
        public void CTMO003(DefinitionPack pack)
        {
            // Arrange
            var options = new ContentTypeMapperOptions();

            // Act
            options.DefinitionPack = pack;

            // Assert
            options.DefinitionPack.Should().Be(pack);
        }
    }
}
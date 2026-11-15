using Ave.Extensions.FileSystem.MimeDetective.Configuration;
using Ave.Extensions.FileSystem.MimeDetective.Services;
using FluentAssertions;
using Xunit;

namespace UnitTests.Extensions.FileSystem.MimeDetective.Services
{
    public class ContentTypeMapperFactoryTests
    {
        [Fact(DisplayName = "CTMF-001: Default factory instance is accessible")]
        public void CTMF001()
        {
            // Arrange & Act
            var factory = ContentTypeMapperFactory.Default;

            // Assert
            factory.Should().NotBeNull();
            factory.Should().BeSameAs(ContentTypeMapperFactory.Default); // Singleton behavior
        }

        [Fact(DisplayName = "CTMF-002: CreateMapper returns configured mapper")]
        public void CTMF002()
        {
            // Arrange
            var factory = ContentTypeMapperFactory.Default;
            var options = new ContentTypeMapperOptions
            {
                MaxBytesToRead = 4096,
                FallbackToExtension = false
            };

            // Act
            var mapper = factory.CreateMapper(options);

            // Assert
            mapper.Should().NotBeNull();
        }

        [Theory(DisplayName = "CTMF-003: CreateMapper works with different definition packs")]
        [InlineData(DefinitionPack.Default)]
        [InlineData(DefinitionPack.Condensed)]
        [InlineData(DefinitionPack.Exhaustive)]
        public void CTMF003(DefinitionPack pack)
        {
            // Arrange
            var factory = ContentTypeMapperFactory.Default;
            var options = new ContentTypeMapperOptions { DefinitionPack = pack };

            // Act
            var mapper = factory.CreateMapper(options);

            // Assert
            mapper.Should().NotBeNull();
        }

        [Fact(DisplayName = "CTMF-004: CreateMapper with null options throws ArgumentNullException")]
        public void CTMF004()
        {
            // Arrange
            var factory = ContentTypeMapperFactory.Default;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => factory.CreateMapper(null!));
        }
    }
}
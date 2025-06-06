using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;
using UnitTests.Extensions.FileSystem.Local.TestHelpers;

namespace UnitTests.Extensions.FileSystem.Local
{
    public class ServiceCollectionExtensionsTests
    {
        [Fact(DisplayName = "SCE-001: AddFileSystem registers IFileSystem with implementation")]
        public void SCE001()
        {
            // Arrange
            var services = new ServiceCollection();
            
            // Act
            services.AddFileSystem();
            var serviceProvider = services.BuildServiceProvider();
            var fileSystem = serviceProvider.GetService<IFileSystem>();
            
            // Assert
            fileSystem.Should().NotBeNull();
            fileSystem.Should().BeOfType<LocalFileSystem>();
        }
        
    }
}
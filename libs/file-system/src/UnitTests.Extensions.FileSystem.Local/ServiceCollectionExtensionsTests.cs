using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Local;

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
        
        [Fact(DisplayName = "SCE-002: AddFileSystemMonitoring registers IFileSystemMonitor")]
        public void SCE002()
        {
            // Arrange
            var services = new ServiceCollection();
            
            // Act
            services.AddFileSystemMonitoring();
            var serviceProvider = services.BuildServiceProvider();
            var monitor = serviceProvider.GetService<IFileSystemMonitor>();
            var fileSystem = serviceProvider.GetService<IFileSystem>();
            
            // Assert
            monitor.Should().NotBeNull();
            fileSystem.Should().NotBeNull();
            fileSystem.Should().BeOfType<LocalFileSystem>();
        }
    }
}
using System;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.InMemory;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace UnitTests.Extensions.FileSystem.InMemory
{
    /// <summary>
    /// Unit tests for InMemory FileSystem ServiceCollection extensions.
    /// </summary>
    public class ServiceCollectionExtensionsTests
    {
        [Fact(DisplayName = "SCE-IM-001: AddInMemoryFileSystem registers services correctly")]
        public void SCEIM001()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            services.AddInMemoryFileSystem();

            // Assert
            var serviceProvider = services.BuildServiceProvider();
            
            var fileSystem = serviceProvider.GetService<IFileSystem>();
            fileSystem.Should().NotBeNull();
            fileSystem.Should().BeOfType<InMemoryFileSystem>();
            
            var readOnlyFileSystem = serviceProvider.GetService<IReadOnlyFileSystem>();
            readOnlyFileSystem.Should().NotBeNull();
            readOnlyFileSystem.Should().BeOfType<InMemoryFileSystem>();
            
            var inMemoryFileSystem = serviceProvider.GetService<InMemoryFileSystem>();
            inMemoryFileSystem.Should().NotBeNull();
            
            // Should be the same instance
            fileSystem.Should().BeSameAs(readOnlyFileSystem);
            fileSystem.Should().BeSameAs(inMemoryFileSystem);
        }

        [Fact(DisplayName = "SCE-IM-002: AddInMemoryFileSystemAsSecondary registers without replacing primary")]
        public void SCEIM002()
        {
            // Arrange
            var services = new ServiceCollection();
            
            // Add a mock primary file system first
            var mockFileSystem = new Mock<IFileSystem>();
            services.AddSingleton(mockFileSystem.Object);
            services.AddSingleton<IReadOnlyFileSystem>(sp => sp.GetRequiredService<IFileSystem>());

            // Act
            services.AddInMemoryFileSystemAsSecondary();

            // Assert
            var serviceProvider = services.BuildServiceProvider();
            
            var primaryFileSystem = serviceProvider.GetService<IFileSystem>();
            primaryFileSystem.Should().BeSameAs(mockFileSystem.Object);
            
            var inMemoryFileSystem = serviceProvider.GetService<InMemoryFileSystem>();
            inMemoryFileSystem.Should().NotBeNull();
            inMemoryFileSystem.Should().NotBeSameAs(primaryFileSystem);
        }

        [Fact(DisplayName = "SCE-IM-003: AddInMemoryFileSystemWithConfiguration configures options")]
        public void SCEIM003()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            services.AddInMemoryFileSystemWithConfiguration(options =>
            {
                options.EnablePathSecurity = true;
                options.AllowedPaths.Add("/allowed");
            });

            // Assert
            var serviceProvider = services.BuildServiceProvider();
            
            var fileSystem = serviceProvider.GetService<IFileSystem>();
            fileSystem.Should().NotBeNull();
            fileSystem.Should().BeOfType<InMemoryFileSystem>();
            
            var readOnlyFileSystem = serviceProvider.GetService<IReadOnlyFileSystem>();
            readOnlyFileSystem.Should().NotBeNull();
            readOnlyFileSystem.Should().BeOfType<InMemoryFileSystem>();
            
            // Should be the same instance
            fileSystem.Should().BeSameAs(readOnlyFileSystem);
        }

        [Fact(DisplayName = "SCE-IM-004: Services are registered as singletons")]
        public void SCEIM004()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            services.AddInMemoryFileSystem();

            // Assert
            var serviceProvider = services.BuildServiceProvider();
            
            var fileSystem1 = serviceProvider.GetService<IFileSystem>();
            var fileSystem2 = serviceProvider.GetService<IFileSystem>();
            
            fileSystem1.Should().BeSameAs(fileSystem2);
            
            var readOnlyFileSystem1 = serviceProvider.GetService<IReadOnlyFileSystem>();
            var readOnlyFileSystem2 = serviceProvider.GetService<IReadOnlyFileSystem>();
            
            readOnlyFileSystem1.Should().BeSameAs(readOnlyFileSystem2);
            readOnlyFileSystem1.Should().BeSameAs(fileSystem1);
        }

        [Fact(DisplayName = "SCE-IM-005: AddInMemoryFileSystem throws on null services")]
        public void SCEIM005()
        {
            // Arrange
            ServiceCollection? services = null;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => services!.AddInMemoryFileSystem());
        }

        [Fact(DisplayName = "SCE-IM-006: AddInMemoryFileSystemAsSecondary throws on null services")]
        public void SCEIM006()
        {
            // Arrange
            ServiceCollection? services = null;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => services!.AddInMemoryFileSystemAsSecondary());
        }

        [Fact(DisplayName = "SCE-IM-007: AddInMemoryFileSystemWithConfiguration throws on null services")]
        public void SCEIM007()
        {
            // Arrange
            ServiceCollection? services = null;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => services!.AddInMemoryFileSystemWithConfiguration());
        }
    }
}
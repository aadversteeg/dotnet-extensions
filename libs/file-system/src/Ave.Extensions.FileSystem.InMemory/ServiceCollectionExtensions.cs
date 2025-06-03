using System;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods for adding in-memory file system services.
    /// </summary>
    public static class InMemoryFileSystemServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the in-memory file system as the primary file system implementation.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddInMemoryFileSystem(this IServiceCollection services)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            // Ensure base file system utilities are registered
            services.AddFileSystemUtilities();
            
            // Register in-memory implementation as the primary file system
            services.AddSingleton<Ave.Extensions.FileSystem.InMemory.InMemoryFileSystem>();
            services.AddSingleton<IFileSystem>(sp => sp.GetRequiredService<Ave.Extensions.FileSystem.InMemory.InMemoryFileSystem>());
            services.AddSingleton<IReadOnlyFileSystem>(sp => sp.GetRequiredService<Ave.Extensions.FileSystem.InMemory.InMemoryFileSystem>());
            
            return services;
        }
        
        /// <summary>
        /// Adds the in-memory file system as a secondary file system implementation (accessible via named registration).
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddInMemoryFileSystemAsSecondary(this IServiceCollection services)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            // Ensure base file system utilities are registered
            services.AddFileSystemUtilities();
            
            // Register in-memory implementation as a named service
            services.AddSingleton<Ave.Extensions.FileSystem.InMemory.InMemoryFileSystem>();
            
            return services;
        }

        /// <summary>
        /// Adds the in-memory file system with custom configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configureFileSystem">Optional configuration for file system features.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddInMemoryFileSystemWithConfiguration(
            this IServiceCollection services,
            Action<FileSystemOptions>? configureFileSystem = null)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            // Register with custom configuration
            services.AddFileSystemUtilities(configureFileSystem);
            
            // Register in-memory implementation
            services.AddSingleton<Ave.Extensions.FileSystem.InMemory.InMemoryFileSystem>();
            services.AddSingleton<IFileSystem>(sp => sp.GetRequiredService<Ave.Extensions.FileSystem.InMemory.InMemoryFileSystem>());
            services.AddSingleton<IReadOnlyFileSystem>(sp => sp.GetRequiredService<Ave.Extensions.FileSystem.InMemory.InMemoryFileSystem>());
            
            return services;
        }
    }
}
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ave.Extensions.FileSystem.DependencyInjection
{
    /// <summary>
    /// Extension methods for setting up file system services in an <see cref="IServiceCollection" />.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the default implementation of <see cref="IFileSystem"/> to the specified <see cref="IServiceCollection" />.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
        /// <returns>The same service collection so that multiple calls can be chained.</returns>
        public static IServiceCollection AddFileSystem(this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.TryAddSingleton<IFileSystem, FileSystemService>();
            
            return services;
        }
        
        /// <summary>
        /// Adds file system monitoring capabilities to the specified <see cref="IServiceCollection" />.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
        /// <returns>The same service collection so that multiple calls can be chained.</returns>
        public static IServiceCollection AddFileSystemMonitoring(this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            // Ensure the file system is registered
            services.AddFileSystem();
            
            // Register factory for IFileSystemMonitor
            services.TryAddTransient<IFileSystemMonitor>(sp =>
            {
                var fileSystem = sp.GetRequiredService<IFileSystem>();
                return fileSystem.CreateMonitorAsync().GetAwaiter().GetResult();
            });
            
            return services;
        }
    }
}
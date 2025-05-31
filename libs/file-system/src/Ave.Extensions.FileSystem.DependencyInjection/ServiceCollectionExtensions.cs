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
            return services.AddFileSystem(null);
        }

        /// <summary>
        /// Adds the default implementation of <see cref="IFileSystem"/> to the specified <see cref="IServiceCollection" /> with configuration.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
        /// <param name="configure">An optional action to configure the file system options.</param>
        /// <returns>The same service collection so that multiple calls can be chained.</returns>
        public static IServiceCollection AddFileSystem(
            this IServiceCollection services, 
            Action<FileSystemOptions>? configure = null)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            var options = new FileSystemOptions();
            configure?.Invoke(options);

            // Register core file system services
            services.TryAddScoped<IFileSystem, FileSystemService>();
            
            // Register IReadOnlyFileSystem as a wrapper around IFileSystem
            if (options.RegisterReadOnlyWrapper)
            {
                services.TryAddScoped<IReadOnlyFileSystem>(sp => sp.GetRequiredService<IFileSystem>());
            }

            // Register path utilities
            if (options.RegisterPathUtilities)
            {
                services.TryAddSingleton<IPathUtilities, PathUtilities>();
            }

            // Register file system visitor
            if (options.RegisterVisitor)
            {
                services.TryAddScoped<IFileSystemVisitor, FileSystemVisitor>();
            }

            // Register monitoring if enabled
            if (options.EnableMonitoring)
            {
                services.TryAddSingleton<IFileSystemMonitor, FileSystemMonitor>();
            }

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
            services.AddFileSystem(options => options.EnableMonitoring = true);
            
            return services;
        }

        /// <summary>
        /// Adds file system visitor capabilities to the specified <see cref="IServiceCollection" />.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
        /// <returns>The same service collection so that multiple calls can be chained.</returns>
        public static IServiceCollection AddFileSystemVisitor(this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            // Ensure the file system is registered with visitor support
            services.AddFileSystem(options => 
            {
                options.RegisterVisitor = true;
                options.RegisterPathUtilities = true;
            });
            
            return services;
        }

        /// <summary>
        /// Adds path utilities to the specified <see cref="IServiceCollection" />.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
        /// <returns>The same service collection so that multiple calls can be chained.</returns>
        public static IServiceCollection AddPathUtilities(this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.TryAddSingleton<IPathUtilities, PathUtilities>();
            
            return services;
        }
    }
}
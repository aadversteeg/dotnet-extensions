using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ave.Extensions.FileSystem.Local.PathMappers;

namespace Ave.Extensions.FileSystem.Local
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
            Action<Configuration.FileSystemOptions>? configure = null)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            var options = new Configuration.FileSystemOptions();
            configure?.Invoke(options);

            // Register shared utilities first
            services.AddFileSystemUtilities(configure);

            // Register path mapper - always required for LocalFileSystem
            // Platform-specific path mapper is automatically determined
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                services.TryAddSingleton<IPathMapper, WindowsPathMapper>();
            }
            else
            {
                services.TryAddSingleton<IPathMapper, UnixPathMapper>();
            }

            // Register local file system implementation
            services.TryAddScoped<IFileSystem>(sp =>
                new LocalFileSystem(
                    sp.GetRequiredService<IPathMapper>(),
                    sp.GetService<Microsoft.Extensions.Logging.ILogger<LocalFileSystem>>()
                        ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalFileSystem>.Instance));


            return services;
        }
        

    }
}
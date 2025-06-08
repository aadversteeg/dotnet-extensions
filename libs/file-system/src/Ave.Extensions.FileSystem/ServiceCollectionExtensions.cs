using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ave.Extensions.FileSystem.Security;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Extension methods for setting up shared file system services in an <see cref="IServiceCollection" />.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds shared file system utilities to the specified <see cref="IServiceCollection" />.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
        /// <param name="configure">An optional action to configure the file system options.</param>
        /// <returns>The same service collection so that multiple calls can be chained.</returns>
        public static IServiceCollection AddFileSystemUtilities(
            this IServiceCollection services, 
            Action<Configuration.FileSystemOptions>? configure = null)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            var options = new Configuration.FileSystemOptions();
            configure?.Invoke(options);
            services.AddSingleton(options);

            // Register security if enabled
            if (options.EnablePathSecurity)
            {
                services.TryAddSingleton<IPathSecurity>(sp => new PathSecurityService());
            }


            // Register IReadOnlyFileSystem as a wrapper around IFileSystem
            if (options.RegisterReadOnlyWrapper)
            {
                services.TryAddScoped<IReadOnlyFileSystem>(sp => 
                {
                    var fileSystem = sp.GetRequiredService<IFileSystem>();
                    return new ReadOnlyFileSystemWrapper(fileSystem);
                });
            }


            return services;
        }



        /// <summary>
        /// Adds path security services to the specified <see cref="IServiceCollection" />.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
        /// <returns>The same service collection so that multiple calls can be chained.</returns>
        public static IServiceCollection AddPathSecurity(this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.TryAddSingleton<IPathSecurity>(sp => new PathSecurityService());
            
            return services;
        }
    }
}
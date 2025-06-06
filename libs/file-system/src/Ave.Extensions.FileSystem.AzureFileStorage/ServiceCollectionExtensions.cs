using System;
using System.Linq;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.AzureFileStorage.Configuration;
using Ave.Extensions.FileSystem.Configuration;
using Ave.Extensions.FileSystem.Local;
using Ave.Extensions.FileSystem.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods for adding Azure File Share file system services.
    /// </summary>
    public static class AzureFileSystemServiceCollectionExtensions
    {
        /// <summary>
        /// Adds Azure File Share as the primary file system implementation.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Configuration action for Azure options.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddAzureFileSystem(
            this IServiceCollection services,
            Action<AzureFileSystemOptions> configure)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            services.Configure(configure);
            
            // Ensure base file system services are registered with canonical paths
            services.AddFileSystem(options =>
            {
                options.UseCanonicalPaths = true; // Azure uses forward slashes
            });
            
            // Register Azure implementation as the primary file system
            services.AddSingleton<Ave.Extensions.FileSystem.AzureFileStorage.AzureFileSystem>();
            services.AddSingleton<IFileSystem>(sp => sp.GetRequiredService<Ave.Extensions.FileSystem.AzureFileStorage.AzureFileSystem>());
            services.AddSingleton<IReadOnlyFileSystem>(sp => sp.GetRequiredService<Ave.Extensions.FileSystem.AzureFileStorage.AzureFileSystem>());
            
            return services;
        }
        
        /// <summary>
        /// Adds Azure File Share as a secondary file system implementation (accessible via named registration).
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Configuration action for Azure options.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddAzureFileSystemAsSecondary(
            this IServiceCollection services,
            Action<AzureFileSystemOptions> configure)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            services.Configure(configure);
            
            // Ensure base file system services are registered
            services.AddFileSystem();
            
            // Register Azure implementation as a named service
            services.AddSingleton<Ave.Extensions.FileSystem.AzureFileStorage.AzureFileSystem>();
            
            return services;
        }

        /// <summary>
        /// Adds Azure File Share with security restrictions.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Configuration action for Azure options.</param>
        /// <param name="configureFileSystem">Optional configuration for base file system features.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddSecureAzureFileSystem(
            this IServiceCollection services,
            Action<AzureFileSystemOptions> configure,
            Action<FileSystemOptions>? configureFileSystem = null)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            services.Configure(configure);
            
            // Register with security enabled
            services.AddFileSystem(options =>
            {
                options.UseCanonicalPaths = true;
                options.EnablePathSecurity = true;
                configureFileSystem?.Invoke(options);
            });
            
            // Override path security to use allowed paths from Azure configuration
            services.AddSingleton<IPathSecurity>(sp =>
            {
                var azureOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureFileSystemOptions>>().Value;
                var allowedCanonicalPaths = azureOptions.AllowedPaths
                    .Select(p => CanonicalPath.Create(p))
                    .Where(r => r.IsSuccess)
                    .Select(r => r.Value);
                return new PathSecurityService(allowedCanonicalPaths);
            });
            
            // Register Azure implementation
            services.AddSingleton<Ave.Extensions.FileSystem.AzureFileStorage.AzureFileSystem>();
            services.AddSingleton<IFileSystem>(sp => sp.GetRequiredService<Ave.Extensions.FileSystem.AzureFileStorage.AzureFileSystem>());
            services.AddSingleton<IReadOnlyFileSystem>(sp => sp.GetRequiredService<Ave.Extensions.FileSystem.AzureFileStorage.AzureFileSystem>());
            
            return services;
        }
    }
}
using System;
using Ave.Extensions.FileSystem.MimeDetective.Configuration;
using Ave.Extensions.FileSystem.MimeDetective.Mapping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ave.Extensions.FileSystem.MimeDetective.Services
{
    /// <summary>
    /// Dependency injection extensions for content type detection services.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds MimeDetective content type detection services.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="configure">Optional action to configure content type mapper options.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddContentTypeDetection(
            this IServiceCollection services,
            Action<ContentTypeMapperOptions>? configure = null)
        {
            var options = new ContentTypeMapperOptions();
            configure?.Invoke(options);

            services.TryAddSingleton(options);
            services.TryAddSingleton<ContentTypeMapperFactory>();
            services.TryAddTransient<ContentTypeMapper>(sp =>
            {
                var factory = sp.GetRequiredService<ContentTypeMapperFactory>();
                var opts = sp.GetRequiredService<ContentTypeMapperOptions>();
                return factory.CreateMapper(opts);
            });

            return services;
        }
    }
}
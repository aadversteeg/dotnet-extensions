using System;
using Ave.Extensions.FileSystem.MimeDetective.Configuration;
using Ave.Extensions.FileSystem.MimeDetective.Mapping;

namespace Ave.Extensions.FileSystem.MimeDetective.Services
{
    /// <summary>
    /// Factory for creating ContentTypeMapper instances.
    /// </summary>
    public class ContentTypeMapperFactory
    {
        private static readonly Lazy<ContentTypeMapperFactory> _default = 
            new Lazy<ContentTypeMapperFactory>(() => new ContentTypeMapperFactory());

        /// <summary>
        /// Gets the default factory instance.
        /// </summary>
        public static ContentTypeMapperFactory Default => _default.Value;

        /// <summary>
        /// Creates a ContentTypeMapper with the specified options.
        /// </summary>
        /// <param name="options">Configuration options for the mapper.</param>
        /// <returns>A new ContentTypeMapper instance.</returns>
        public ContentTypeMapper CreateMapper(ContentTypeMapperOptions options)
        {
            // For now, create a mapper without MimeDetective inspector until API issues are resolved
            return new ContentTypeMapper(null, options);
        }
    }
}
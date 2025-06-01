using System;
using System.Collections.Generic;

namespace Ave.Extensions.FileSystem.AzureStorage.Configuration
{
    /// <summary>
    /// Configuration options for Azure File Share file system.
    /// </summary>
    public class AzureFileSystemOptions
    {
        /// <summary>
        /// Azure Storage connection string
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;
        
        /// <summary>
        /// Alternative to connection string: use account name and key
        /// </summary>
        public string? AccountName { get; set; }
        
        /// <summary>
        /// Account key for authentication (used with AccountName)
        /// </summary>
        public string? AccountKey { get; set; }
        
        /// <summary>
        /// Azure File Share name
        /// </summary>
        public required string ShareName { get; set; }
        
        /// <summary>
        /// Optional base directory within the share
        /// </summary>
        public string? BaseDirectory { get; set; }
        
        /// <summary>
        /// Create share if it doesn't exist
        /// </summary>
        public bool CreateShareIfNotExists { get; set; } = false;
        
        /// <summary>
        /// Allowed paths for security validation
        /// </summary>
        public List<string> AllowedPaths { get; set; } = new();
        
        /// <summary>
        /// Timeout for operations
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
        
        /// <summary>
        /// Gets the connection string (builds it from account name/key if needed)
        /// </summary>
        public string GetConnectionString()
        {
            if (!string.IsNullOrEmpty(ConnectionString))
                return ConnectionString;
                
            if (!string.IsNullOrEmpty(AccountName) && !string.IsNullOrEmpty(AccountKey))
                return $"DefaultEndpointsProtocol=https;AccountName={AccountName};AccountKey={AccountKey};EndpointSuffix=core.windows.net";
                
            throw new InvalidOperationException("Either ConnectionString or AccountName/AccountKey must be provided");
        }
    }
}
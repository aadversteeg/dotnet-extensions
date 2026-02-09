namespace IntegrationTests.Extensions.FileSystem.AzureFileStorage.Configuration
{
    /// <summary>
    /// Test configuration helper for Azure File Storage integration tests
    /// </summary>
    public class TestConfiguration
    {
        public AzureFileStorageTestSettings AzureFileStorage { get; set; } = new();
        public bool SkipIntegrationTests { get; set; }

        public bool IsConfigured()
        {
            return !string.IsNullOrEmpty(AzureFileStorage.AccountName) &&
                   !string.IsNullOrEmpty(AzureFileStorage.AccountKey) &&
                   !string.IsNullOrEmpty(AzureFileStorage.ShareName);
        }
    }

    public class AzureFileStorageTestSettings
    {
        public string AccountName { get; set; } = "";
        public string AccountKey { get; set; } = "";
        public string ShareName { get; set; } = "";
        public string BaseDirectory { get; set; } = "integration-tests";
        public bool CreateShareIfNotExists { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 30;
    }
}
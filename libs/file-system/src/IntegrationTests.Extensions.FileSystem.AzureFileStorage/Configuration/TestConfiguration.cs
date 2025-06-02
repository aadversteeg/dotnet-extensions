using Microsoft.Extensions.Configuration;

namespace IntegrationTests.Extensions.FileSystem.AzureFileStorage.Configuration
{
    /// <summary>
    /// Test configuration helper for Azure File Storage integration tests
    /// </summary>
    public class TestConfiguration
    {
        public AzureFileStorageTestSettings AzureFileStorage { get; set; } = new();
        public bool SkipIntegrationTests { get; set; }

        public static TestConfiguration Load()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.test.json", optional: true)
                .AddUserSecrets<TestConfiguration>(optional: true)
                .AddEnvironmentVariables("AZURE_FILESTORAGE_")
                .Build();

            var testConfig = new TestConfiguration();
            configuration.Bind(testConfig);

            // Also check for individual environment variables with common naming
            var accountName = Environment.GetEnvironmentVariable("AZURE_FILESTORAGE_ACCOUNT_NAME") 
                            ?? Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_NAME")
                            ?? Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT");
            var accountKey = Environment.GetEnvironmentVariable("AZURE_FILESTORAGE_ACCOUNT_KEY")
                           ?? Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_KEY")
                           ?? Environment.GetEnvironmentVariable("AZURE_STORAGE_KEY");
            var shareName = Environment.GetEnvironmentVariable("AZURE_FILESTORAGE_SHARE_NAME")
                          ?? Environment.GetEnvironmentVariable("AZURE_STORAGE_SHARE_NAME")
                          ?? Environment.GetEnvironmentVariable("AZURE_STORAGE_SHARE");

            if (!string.IsNullOrEmpty(accountName))
                testConfig.AzureFileStorage.AccountName = accountName;
            if (!string.IsNullOrEmpty(accountKey))
                testConfig.AzureFileStorage.AccountKey = accountKey;
            if (!string.IsNullOrEmpty(shareName))
                testConfig.AzureFileStorage.ShareName = shareName;

            // Check if we should skip tests
            var skipTests = Environment.GetEnvironmentVariable("SKIP_INTEGRATION_TESTS");
            if (bool.TryParse(skipTests, out var skip))
                testConfig.SkipIntegrationTests = skip;

            return testConfig;
        }

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
# User Secrets Setup for Azure File Storage Integration Tests

## What are User Secrets?

User Secrets is a .NET feature that allows you to store sensitive configuration data locally on your development machine without committing it to source control. The secrets are stored in a JSON file in your user profile directory.

## Setting Up User Secrets

### 1. Navigate to the Integration Test Project

```bash
cd libs/file-system/integration-tests/IntegrationTests.AzureFileStorage
```

### 2. Initialize User Secrets (if not already done)

```bash
dotnet user-secrets init
```

The project already has a `UserSecretsId` configured, so this step is optional.

### 3. Set Your Azure Storage Credentials

```bash
# Required settings
dotnet user-secrets set "AzureFileStorage:AccountName" "your_storage_account_name"
dotnet user-secrets set "AzureFileStorage:AccountKey" "your_storage_account_key"
dotnet user-secrets set "AzureFileStorage:ShareName" "integration-tests"

# Optional settings
dotnet user-secrets set "AzureFileStorage:CreateShareIfNotExists" "true"
dotnet user-secrets set "AzureFileStorage:BaseDirectory" "integration-tests"
dotnet user-secrets set "AzureFileStorage:TimeoutSeconds" "30"
```

### 4. Verify Your Settings

```bash
dotnet user-secrets list
```

Expected output:
```
AzureFileStorage:AccountName = your_storage_account_name
AzureFileStorage:AccountKey = your_storage_account_key
AzureFileStorage:ShareName = integration-tests
AzureFileStorage:CreateShareIfNotExists = true
```

## Finding Your Azure Storage Credentials

### In Azure Portal:

1. Go to your **Storage Account**
2. Navigate to **Security + networking** → **Access keys**
3. Copy the **Storage account name** and one of the **Key** values

### Example:

If your storage account is named `mystorageaccount123` and your key is `abc123...xyz789==`, you would run:

```bash
dotnet user-secrets set "AzureFileStorage:AccountName" "mystorageaccount123"
dotnet user-secrets set "AzureFileStorage:AccountKey" "abc123...xyz789=="
dotnet user-secrets set "AzureFileStorage:ShareName" "integration-tests"
```

## Running Tests with User Secrets

Once configured, simply run:

```bash
dotnet test
```

The tests will automatically pick up your user secrets configuration.

## Alternative Environment Variable Configuration

You can also use environment variables instead of or in addition to user secrets:

```bash
# Primary environment variables
export AZURE_FILESTORAGE_ACCOUNT_NAME="your_storage_account_name"
export AZURE_FILESTORAGE_ACCOUNT_KEY="your_storage_account_key"
export AZURE_FILESTORAGE_SHARE_NAME="integration-tests"

# Alternative environment variables (for compatibility)
export AZURE_STORAGE_ACCOUNT_NAME="your_storage_account_name"
export AZURE_STORAGE_ACCOUNT_KEY="your_storage_account_key"
export AZURE_STORAGE_SHARE_NAME="integration-tests"
```

## Removing User Secrets

To remove all secrets:

```bash
dotnet user-secrets clear
```

To remove a specific secret:

```bash
dotnet user-secrets remove "AzureFileStorage:AccountKey"
```

## Configuration Priority

The integration tests load configuration in this order (later sources override earlier ones):

1. `appsettings.test.json`
2. **User Secrets** ← Your settings
3. Environment Variables
4. Individual environment variable aliases

This means your user secrets will override the template `appsettings.test.json` but can still be overridden by environment variables if needed.

## Security Benefits

- ✅ **Never committed to git** - Secrets stay on your local machine
- ✅ **Encrypted storage** - Stored securely in your user profile
- ✅ **Project-specific** - Each project has its own secret store
- ✅ **Team-friendly** - Each developer can have different credentials
- ✅ **Cross-platform** - Works on Windows, macOS, and Linux

## Troubleshooting

### "No secrets found" or tests are skipped

1. Verify you're in the correct directory: `libs/file-system/integration-tests/IntegrationTests.AzureFileStorage`
2. Check your secrets: `dotnet user-secrets list`
3. Ensure the key names are exact: `AzureFileStorage:AccountName` (case-sensitive)

### "Authentication failed" errors

1. Verify your storage account name is correct
2. Regenerate your access key in Azure Portal and update user secrets
3. Ensure your storage account has File Share service enabled

### "Share not found" errors

1. Either create the share manually in Azure Portal
2. Or set `"AzureFileStorage:CreateShareIfNotExists"` to `"true"`

## IMPORTANT WARNING

⚠️ **These tests will CLEAR THE ENTIRE AZURE FILE SHARE before running.**

Make sure to use a dedicated test storage account and share name. Do NOT use a production storage account or share that contains important data, as it will be completely deleted during test execution.

Each test run empties the configured Azure File Share to ensure clean test isolation.
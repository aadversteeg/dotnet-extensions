# Azure File Storage Integration Tests

This project contains comprehensive integration tests for the Azure File Storage implementation of the IFileSystem interface.

## Overview

These integration tests verify that the `Ave.Extensions.FileSystem.AzureFileStorage` package correctly implements all IFileSystem operations against a real Azure File Share. The tests cover:

- File operations (create, read, write, delete, exists)
- Directory operations (create, list, delete, exists)
- Stream operations (read/write streams)
- Metadata retrieval
- Enumeration with filtering
- Error handling
- Unicode and large file support
- Cross-platform path handling

## ⚠️ IMPORTANT WARNING

**These tests will CLEAR THE ENTIRE AZURE FILE SHARE before running.**

Each test run completely empties the configured Azure File Share to ensure clean test isolation. Use a dedicated test storage account and share name. **DO NOT** use a production storage account or share that contains important data.

## Setup

### Prerequisites

1. Azure Storage Account with File Share service enabled
2. .NET 8.0 SDK or later

### Configuration

The tests can be configured using:

1. **User Secrets** (recommended for local development)
2. Environment variables
3. `appsettings.test.json` file

See [USER_SECRETS_SETUP.md](USER_SECRETS_SETUP.md) for detailed setup instructions.

### Quick Setup with User Secrets

1. Navigate to this project directory:
   ```bash
   cd libs/file-system/integration-tests/IntegrationTests.AzureFileStorage
   ```

2. Set your Azure credentials:
   ```bash
   dotnet user-secrets set "AzureFileStorage:AccountName" "your_storage_account_name"
   dotnet user-secrets set "AzureFileStorage:AccountKey" "your_storage_account_key"
   ```

   Note: `ShareName` defaults to `integration-tests` in `appsettings.test.json`.

3. Run the tests:
   ```bash
   dotnet test
   ```

## Test Structure

### Test Categories

#### Core Operations (AFSIT)
- **AFSIT-001 to AFSIT-007**: Basic file and directory operations
- **AFSIT-008 to AFSIT-011**: Listing and enumeration operations
- **AFSIT-013 to AFSIT-014**: Delete operations
- **AFSIT-015 to AFSIT-017**: Special content handling (large files, Unicode, empty files)

#### Move/Copy Operations (AFSMC)
- **AFSMC-001 to AFSMC-002**: File copy and move operations
- **AFSMC-003 to AFSMC-005**: Directory copy and move operations
- **AFSMC-006 to AFSMC-007**: Cancellation and overwrite scenarios

### Test Features

- **Clean isolation**: Each test uses a unique timestamp-based directory
- **Direct Azure verification**: Tests verify results using Azure Storage SDK directly
- **Comprehensive cleanup**: Automatic cleanup of created files and directories
- **Skip logic**: Tests are automatically skipped if not configured
- **Cross-platform**: Tests work on Windows, macOS, and Linux

## Running Tests

### Local Development

```bash
# Run all tests
dotnet test

# Run with verbose output
dotnet test --logger:console;verbosity=detailed

# Run a specific test
dotnet test --filter "DisplayName~AFSIT-001"
```

### CI/CD

Set environment variables in your build pipeline:

```bash
export AZURE_FILESTORAGE_ACCOUNT_NAME="your_account"
export AZURE_FILESTORAGE_ACCOUNT_KEY="your_key"
export AZURE_FILESTORAGE_SHARE_NAME="integration-tests"
dotnet test
```

### Skipping Tests

To skip integration tests:

```bash
export SKIP_INTEGRATION_TESTS=true
dotnet test
```

## Configuration Options

| Setting | Description | Default |
|---------|-------------|---------|
| `AccountName` | Azure Storage account name | Required |
| `AccountKey` | Azure Storage account key | Required |
| `ShareName` | Azure File Share name | Required |
| `BaseDirectory` | Base directory within share | "integration-tests" |
| `CreateShareIfNotExists` | Create share if missing | true |
| `TimeoutSeconds` | Operation timeout | 30 |

## Architecture

The integration tests use:

- **xUnit** as the test framework
- **FluentAssertions** for readable assertions
- **Azure.Storage.Files.Shares** for direct Azure verification
- **Microsoft.Extensions.DependencyInjection** for service registration
- **IAsyncLifetime** for proper test setup and cleanup

## Troubleshooting

### Tests are skipped

- Verify user secrets are set correctly: `dotnet user-secrets list`
- Check environment variables
- Ensure configuration keys are exact (case-sensitive)

### Authentication errors

- Regenerate access key in Azure Portal
- Verify storage account name spelling
- Ensure File Share service is enabled

### Share not found errors

- Set `CreateShareIfNotExists` to `true`
- Manually create the share in Azure Portal
- Verify share name spelling

### Network or timeout errors

- Increase `TimeoutSeconds` setting
- Check network connectivity to Azure
- Verify storage account region accessibility

## Best Practices

1. **Use dedicated test resources**: Never run against production storage
2. **Monitor test costs**: Azure File Share operations incur charges
3. **Clean up regularly**: Remove old test shares if accumulated
4. **Use descriptive share names**: Include "test" or "integration" in names
5. **Set appropriate timeouts**: Adjust based on network conditions

## Contributing

When adding new tests:

1. Follow the naming convention: `AFSIT-###`
2. Include descriptive DisplayName attributes
3. Add proper cleanup to `_createdFiles` and `_createdDirectories`
4. Include direct Azure verification where applicable
5. Test both success and error scenarios
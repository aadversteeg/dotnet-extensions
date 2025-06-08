# Build script for FileSystem samples

param(
    [string]$Configuration = "Release",
    [switch]$Test,
    [switch]$Benchmark,
    [switch]$Clean,
    [switch]$Restore
)

$ErrorActionPreference = "Stop"

function Write-Status {
    param([string]$Message, [string]$Color = "Green")
    Write-Host $Message -ForegroundColor $Color
}

function Write-Error {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Red
}

try {
    Write-Status "Building FileSystem Samples..."
    Write-Host "Configuration: $Configuration"
    Write-Host "Test: $Test"
    Write-Host "Benchmark: $Benchmark"
    Write-Host "Clean: $Clean"
    Write-Host "Restore: $Restore"
    Write-Host ""

    # Clean if requested
    if ($Clean) {
        Write-Status "Cleaning solution..."
        dotnet clean FileSystemSamples.sln -c $Configuration
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Clean failed"
            exit $LASTEXITCODE
        }
    }

    # Restore if requested or if clean was performed
    if ($Restore -or $Clean) {
        Write-Status "Restoring packages..."
        dotnet restore FileSystemSamples.sln
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Restore failed"
            exit $LASTEXITCODE
        }
    }

    # Build all projects
    Write-Status "Building solution..."
    dotnet build FileSystemSamples.sln -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed"
        exit $LASTEXITCODE
    }

    # Run tests if requested
    if ($Test) {
        Write-Status "`nRunning unit tests..."
        dotnet test Common.Tests/Common.Tests.csproj -c $Configuration --no-build --verbosity normal
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Unit tests failed"
            exit $LASTEXITCODE
        }

        Write-Status "Running integration tests..."
        dotnet test Integration.Tests/Integration.Tests.csproj -c $Configuration --no-build --verbosity normal
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Integration tests failed"
            exit $LASTEXITCODE
        }
    }

    # Run benchmarks if requested
    if ($Benchmark) {
        Write-Status "`nRunning benchmarks..."
        dotnet run --project Common.Benchmarks/Common.Benchmarks.csproj -c Release --no-build
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Benchmarks failed"
            exit $LASTEXITCODE
        }
    }

    Write-Status "`nBuild completed successfully!"
    Write-Host "All projects built and tested successfully." -ForegroundColor Green

} catch {
    Write-Error "Build script failed: $_"
    exit 1
}
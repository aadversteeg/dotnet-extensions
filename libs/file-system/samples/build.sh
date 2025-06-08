#!/bin/bash

# Build script for FileSystem samples

CONFIGURATION=${1:-Release}
RUN_TESTS=${2:-false}
RUN_BENCHMARKS=${3:-false}
CLEAN=${4:-false}
RESTORE=${5:-false}

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

function write_status {
    echo -e "${GREEN}$1${NC}"
}

function write_error {
    echo -e "${RED}$1${NC}" >&2
}

function write_warning {
    echo -e "${YELLOW}$1${NC}"
}

set -e

write_status "Building FileSystem Samples..."
echo "Configuration: $CONFIGURATION"
echo "Run Tests: $RUN_TESTS"
echo "Run Benchmarks: $RUN_BENCHMARKS"
echo "Clean: $CLEAN"
echo "Restore: $RESTORE"
echo ""

# Clean if requested
if [ "$CLEAN" = true ]; then
    write_status "Cleaning solution..."
    dotnet clean FileSystemSamples.sln -c $CONFIGURATION
fi

# Restore if requested or if clean was performed
if [ "$RESTORE" = true ] || [ "$CLEAN" = true ]; then
    write_status "Restoring packages..."
    dotnet restore FileSystemSamples.sln
fi

# Build all projects
write_status "Building solution..."
dotnet build FileSystemSamples.sln -c $CONFIGURATION --no-restore

# Run tests if requested
if [ "$RUN_TESTS" = true ]; then
    write_status "\nRunning unit tests..."
    dotnet test Common.Tests/Common.Tests.csproj -c $CONFIGURATION --no-build --verbosity normal
    
    write_status "Running integration tests..."
    dotnet test Integration.Tests/Integration.Tests.csproj -c $CONFIGURATION --no-build --verbosity normal
fi

# Run benchmarks if requested
if [ "$RUN_BENCHMARKS" = true ]; then
    write_status "\nRunning benchmarks..."
    dotnet run --project Common.Benchmarks/Common.Benchmarks.csproj -c Release --no-build
fi

write_status "\nBuild completed successfully!"
echo -e "${GREEN}All projects built and tested successfully.${NC}"
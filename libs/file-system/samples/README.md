# FileSystem Sample Applications

This folder contains simple console applications that demonstrate the use of the FileSystem abstraction library with System.CommandLine for command-line parsing and dependency injection.

## Prerequisites

- .NET 8.0 SDK
- Build the main solution first to ensure all dependencies are available

## Building the Samples

From the samples directory:

```bash
cd samples
dotnet build
```

Or build individual samples:

```bash
cd FileCounter
dotnet build
```

## Sample Applications

### 1. FileCounter

Counts files and directories with various filtering options.

**Usage:**
```bash
# Count all files in current directory
FileCounter

# Count only .cs files
FileCounter --path "D:\Projects\MyApp" --extension .cs

# Count files larger than 1MB
FileCounter --path "D:\Downloads" --min-size 1048576

# Count non-recursively (only immediate children)
FileCounter --path "D:\Documents" --recursive false
```

**Options:**
- `--path, -p`: Path to count files in (default: current directory)
- `--extension, -e`: Filter by file extension (e.g., .cs, .txt)
- `--min-size`: Minimum file size in bytes
- `--max-size`: Maximum file size in bytes
- `--recursive, -r`: Count recursively (default: true)

### 2. DirSize

Calculates directory sizes recursively and shows the largest directories.

**Usage:**
```bash
# Show top 10 largest directories
DirSize --path "D:\Projects"

# Show directories larger than 100MB
DirSize --path "C:\Users" --min-size 100MB

# Show top 20 directories sorted by file count
DirSize --path "D:\Code" --top 20 --sort FileCount

# Limit depth of analysis
DirSize --path "C:\" --depth 3
```

**Options:**
- `--path, -p`: Path to analyze (default: current directory)
- `--top, -t`: Show top N largest directories (default: 10)
- `--min-size, -m`: Minimum size to display (e.g., 1MB, 100KB)
- `--depth, -d`: Maximum depth to analyze
- `--sort, -s`: Sort results by Size, Name, or FileCount (default: Size)

**Size Format:**
- Supports B, KB, MB, GB, TB (e.g., "1.5GB", "500MB", "100KB")

### 3. FileSearch

Search for files with various criteria including patterns, size, and modification date.

**Usage:**
```bash
# Search for all .log files
FileSearch --path "D:\Logs" --pattern "*.log"

# Search using regex
FileSearch --path "D:\Projects" --regex "^test.*\.cs$"

# Find files modified in the last week
FileSearch --path "D:\Documents" --modified-after "2024-01-01"

# Find large files
FileSearch --path "D:\Downloads" --larger-than 100MB

# Combined criteria
FileSearch --path "D:\Projects" --pattern "*.cs" --modified-after "2024-01-01" --smaller-than 1MB
```

**Options:**
- `--path, -p`: Path to search in (default: current directory)
- `--pattern, -n`: File name pattern with wildcards (default: *)
- `--regex, -r`: Regular expression for file names
- `--modified-after, -ma`: Files modified after this date
- `--modified-before, -mb`: Files modified before this date
- `--larger-than, -l`: Files larger than size (e.g., 1MB)
- `--smaller-than, -s`: Files smaller than size (e.g., 1MB)
- `--max-results, -m`: Maximum number of results (default: 100)
- `--include-hidden, -h`: Include hidden files (default: false)

## Common Features

All applications share these common features:

1. **Structured Output**: Results are written to stdout, logs to stderr
2. **Error Handling**: Graceful error handling with meaningful messages
3. **Cancellation**: Support for Ctrl+C cancellation
4. **Path Handling**: Automatic conversion between Windows and canonical paths
5. **Memory Efficient**: Uses traversal strategies optimized for each use case

## Architecture Notes

These samples demonstrate:

- **System.CommandLine**: Modern command-line parsing with strong typing
- **Dependency Injection**: Using Microsoft.Extensions.DependencyInjection
- **ITraversalHandler**: Custom handlers for different traversal operations
- **IFileSystemEntryMapper**: Simple pass-through mapper for these examples
- **Result Pattern**: Functional error handling without exceptions
- **Logging**: Structured logging to stderr using Microsoft.Extensions.Logging

## Extending the Samples

To create your own file system tools:

1. Implement `ITraversalHandler<T>` for your custom logic
2. Optionally implement `IFileSystemEntryMapper<T>` for custom transformations
3. Use the `IFileSystem` interface for all file operations
4. Choose appropriate `TraversalStrategyHint` for your use case:
   - `Sequential`: Memory-efficient, one file at a time
   - `Parallel`: Performance-focused for CPU-bound operations
   - `Search`: Optimized for finding files quickly

# FileCounter

FileCounter is a cross-platform console application that demonstrates file system traversal and counting operations using the Ave.Extensions.FileSystem library. It showcases various traversal strategies, filtering options, and export formats.

## Features

- **Multiple Handler Types**: Simple, Detailed, and Summary counting modes
- **Flexible Filtering**: Filter by file extension, size range
- **Export Formats**: Text, JSON, CSV, and XML output
- **Traversal Strategies**: Sequential, Parallel, Memory-Efficient, and Search optimized
- **Cross-Platform**: Works on Windows, Linux, and macOS
- **Performance Metrics**: Tracks processing speed and performance statistics

## Usage

### Basic Usage

```bash
# Count files in current directory
FileCounter

# Count files in specific path
FileCounter --path /home/user/projects

# Count files with extension filter
FileCounter --path ./src --extension .cs

# Non-recursive counting (top-level only)
FileCounter --path . --recursive false
```

### Advanced Usage

```bash
# Count with size constraints
FileCounter --path . --min-size 1024 --max-size 1048576

# Use parallel traversal strategy
FileCounter --path /large/directory --strategy Parallel

# Generate detailed breakdown by extension
FileCounter --path ./src --handler Detailed

# Export results to JSON
FileCounter --path . --handler Summary --format Json --output results.json

# Complete example with all options
FileCounter \
  --path /home/user/projects \
  --extension .cs \
  --min-size 100 \
  --max-size 10485760 \
  --recursive true \
  --strategy Parallel \
  --handler Summary \
  --format Json \
  --output project-stats.json
```

## Command Line Options

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `--path` | `-p` | Path to count files in | Current directory |
| `--extension` | `-e` | Filter by file extension (e.g., .cs, .txt) | None |
| `--min-size` | | Minimum file size in bytes | 0 |
| `--max-size` | | Maximum file size in bytes | No limit |
| `--recursive` | `-r` | Count recursively | true |
| `--strategy` | `-s` | Traversal strategy | Auto |
| `--handler` | `-h` | Handler type | Simple |
| `--format` | | Output format | Text |
| `--output` | `-o` | Output file path | stdout |
| `--max-extensions` | | Max extensions in text format (0=all) | 20 |
| `--log-level` | | Logging level (Trace/Debug/Information/Warning/Error/Critical) | Warning |
| `--max-depth` | | Maximum traversal depth (null=unlimited) | null |

### Handler Types

- **Simple**: Basic counting with minimal memory footprint
- **Detailed**: Extended counting with per-extension breakdown
- **Summary**: Comprehensive statistics with performance metrics

### Traversal Strategies

- **Auto**: Automatically select the best strategy
- **Search**: Optimized for finding files quickly
- **FullScan**: Optimized for complete directory scanning
- **MemoryEfficient**: Minimal memory usage for large directories
- **Parallel**: Use multiple threads for faster processing

### Output Formats

- **Text**: Human-readable console output (default)
- **JSON**: Structured data for processing
- **CSV**: Tabular data for spreadsheets
- **XML**: Hierarchical data representation
- **Markdown**: Documentation-friendly format with tables

## Sample Output

### Simple Handler
```
Files:       1,234
Directories: 56
Total Size:  1.23 GB
```

### Detailed Handler
```
File Count by Extension:
.cs     1,234 files    12.3 MB
.json     456 files     2.1 MB
.xml      123 files     5.6 MB
.txt       89 files     1.2 MB
[other]   234 files     8.9 MB

Total: 2,136 files (30.1 MB) in 156 directories
```

### Summary Handler
```
Files:       1,234
Directories: 56  
Total Size:  1.23 GB
Max Depth:   8
Largest:     database.db (156.7 MB)
Oldest:      2020-01-15 10:30:00
Newest:      2024-03-20 14:22:00
Performance: 1,005 files/second
```

### JSON Format
```json
{
  "fileCount": 1234,
  "directoryCount": 56,
  "totalSize": 1234567890,
  "summary": {
    "fileCount": 1234,
    "directoryCount": 56,
    "totalSize": 1234567890,
    "formattedSize": "1.23 GB",
    "maxDepth": 8,
    "largestFile": {
      "name": "database.db",
      "size": 156700000
    },
    "dateRange": {
      "oldest": "2020-01-15T10:30:00.0000000Z",
      "newest": "2024-03-20T14:22:00.0000000Z"
    },
    "performance": {
      "elapsedSeconds": 1.23,
      "filesPerSecond": 1005.0
    }
  }
}
```

## Building and Running

### Prerequisites

- .NET 8.0 SDK or later

### Build

```bash
cd samples/FileCounter
dotnet build
```

### Run

```bash
dotnet run -- [options]
```

### Publish

```bash
# Create self-contained executable
dotnet publish -c Release -r win-x64 --self-contained

# Or platform-specific
dotnet publish -c Release -r linux-x64 --self-contained
dotnet publish -c Release -r osx-x64 --self-contained
```

## Architecture

The FileCounter application demonstrates several key concepts from the Ave.Extensions.FileSystem library:

### Key Components

1. **Counting Handlers**: Implement `ITraversalHandler<FileSystemEntry>` to collect statistics during traversal
2. **Export System**: Pluggable exporters for different output formats
3. **Service Architecture**: Uses dependency injection for loose coupling
4. **Command Line Interface**: Built with System.CommandLine for robust argument parsing

### Design Patterns

- **Strategy Pattern**: Different traversal strategies for various scenarios
- **Factory Pattern**: Handler and exporter creation
- **Dependency Injection**: Service registration and resolution
- **Result Pattern**: Error handling without exceptions

### Thread Safety

All handlers are designed to be thread-safe using `Interlocked` operations, making them suitable for parallel traversal strategies.

## Performance Tips

1. **Use Parallel Strategy**: For SSDs and fast storage, parallel traversal can significantly improve performance
2. **Memory-Efficient Strategy**: For very large directory trees or limited memory systems
3. **Extension Filtering**: Apply filters early to reduce processing overhead
4. **Size Constraints**: Use min/max size filters to focus on relevant files

## Error Handling

The application gracefully handles common scenarios:

- **Access Denied**: Continues processing other directories
- **Invalid Paths**: Validates paths before processing
- **Large Files**: Handles files of any size within system limits
- **Network Paths**: Supports UNC paths and network drives

## Integration

FileCounter can be easily integrated into build systems, monitoring scripts, or data analysis pipelines:

```bash
# Generate project statistics during CI/CD
FileCounter --path ./src --handler Summary --format Json --output stats.json

# Monitor directory changes
FileCounter --path /var/log --handler Simple --format Csv >> daily-stats.csv

# Analyze code distribution
FileCounter --path ./project --handler Detailed --format Xml --output code-analysis.xml
```

## Contributing

This sample application demonstrates the capabilities of the Ave.Extensions.FileSystem library. Feel free to extend it with additional features such as:

- Date range filtering
- Regular expression pattern matching
- Real-time monitoring mode
- Progress reporting for large operations
- Custom output formatters
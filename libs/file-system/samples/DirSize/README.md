# DirSize - Directory Size Analyzer

DirSize is a cross-platform console application that calculates directory sizes recursively using the Ave.Extensions.FileSystem library. It demonstrates advanced traversal techniques with size aggregation, multiple display formats, and efficient memory usage through stack-based tracking.

## Features

- **Recursive Directory Analysis**: Calculate total sizes for directories and their subdirectories
- **Multiple Handler Types**: Simple, Detailed, and Tree view handlers
- **Flexible Output Formats**: Text, JSON, CSV, XML, and Markdown export options
- **Size Filtering**: Filter directories by minimum size with unit support (B, KB, MB, GB, TB)
- **Sorting Options**: Sort by size, name, or file count
- **Performance Tracking**: Monitor processing time and handle errors gracefully
- **Tree Visualization**: ASCII tree view of directory hierarchy with sizes

## Usage

### Basic Usage

```bash
# Analyze current directory
DirSize

# Analyze specific path
DirSize --path /home/user/projects

# Show top 20 largest directories
DirSize --path ~/Documents --top 20
```

### Filtering and Sorting

```bash
# Filter by minimum size
DirSize --path /var --min-size 100MB

# Sort by file count instead of size
DirSize --path . --sort FileCount

# Limit traversal depth
DirSize --path /usr --max-depth 3
```

### Different Views

```bash
# Simple view (default)
DirSize --handler Simple

# Detailed view with additional statistics
DirSize --handler Detailed

# Tree view with hierarchy visualization
DirSize --handler Tree
```

### Export Options

```bash
# Export to JSON
DirSize --path . --format Json --output sizes.json

# Export to CSV for spreadsheet analysis
DirSize --path ~/projects --format Csv --output project-sizes.csv

# Generate Markdown report
DirSize --path /var/log --format Markdown --output log-sizes.md
```

### Advanced Options

```bash
# Verbose logging for debugging
DirSize --path /large/directory --log-level Debug --verbose

# Include hidden directories
DirSize --path ~ --include-hidden

# Use parallel traversal strategy
DirSize --path /mnt/data --strategy Parallel

# Hide percentage columns
DirSize --path . --show-percentage false
```

## Command-Line Options

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `--path` | `-p` | Path to analyze | Current directory |
| `--top` | `-t` | Show top N largest directories | 10 |
| `--min-size` | `-m` | Minimum size to display (e.g., 1MB, 100KB) | None |
| `--max-depth` | `-d` | Maximum depth to analyze | Unlimited |
| `--sort` | `-s` | Sort by: Size, Name, FileCount | Size |
| `--strategy` | | Traversal strategy: Auto, Search, FullScan, MemoryEfficient, Parallel | Auto |
| `--handler` | `-h` | Handler type: Simple, Detailed, Tree | Simple |
| `--format` | | Output format: Text, Json, Csv, Xml, Markdown | Text |
| `--output` | `-o` | Output file path (stdout if not specified) | None |
| `--verbose` | `-v` | Show detailed error information | false |
| `--log-level` | | Logging level: Trace, Debug, Information, Warning, Error, Critical | Warning |
| `--show-percentage` | | Show percentage of total size | true |
| `--include-hidden` | | Include hidden directories | false |

## Output Formats

### Text Format (Default)
```
Size            Files      Percent  Directory
--------------------------------------------------
2.34 GB         1,247      45.2%    /home/user/projects/large-project
856.7 MB        423        16.5%    /home/user/projects/node_modules
234.1 MB        89         4.5%     /home/user/projects/build

Total: 5.18 GB in 3,456 files
Directories: 234
Processing time: 2.3s
```

### Tree Format
```
projects [5.18 GB] (3,456 files)
├── large-project [2.34 GB] (1,247 files)
│   ├── src [1.89 GB] (892 files)
│   └── data [456.2 MB] (355 files)
├── node_modules [856.7 MB] (423 files)
└── build [234.1 MB] (89 files)
```

### JSON Format
```json
{
  "summary": {
    "totalSize": 5564080128,
    "totalFiles": 3456,
    "totalDirectories": 234,
    "processingTimeMs": 2345.6,
    "errorCount": 0
  },
  "directories": [
    {
      "path": "/home/user/projects/large-project",
      "size": 2513803264,
      "fileCount": 1247,
      "percentage": 45.2
    }
  ]
}
```

## Error Handling

DirSize gracefully handles common filesystem errors:

- **Access Denied**: Directories that can't be accessed are skipped and counted as errors
- **Partial Results**: Analysis continues even if some directories are inaccessible
- **Error Reporting**: All errors are logged and can be included in exports
- **Verbose Mode**: Use `--verbose` for detailed error information

## Performance

- **Memory Efficient**: Uses stack-based tracking to minimize memory usage
- **Concurrent Processing**: Supports parallel traversal strategies for large datasets
- **Progress Tracking**: Real-time elapsed time and speed metrics
- **Depth Limiting**: Prevent excessive recursion with `--max-depth`

## Examples

### Find Largest Directories in System
```bash
# Find top 10 largest directories in /usr
DirSize --path /usr --top 10 --min-size 100MB

# Export system analysis to CSV
DirSize --path / --format Csv --output system-sizes.csv --max-depth 3
```

### Project Analysis
```bash
# Analyze a code project with tree view
DirSize --path ~/projects/myapp --handler Tree --max-depth 4

# Find large node_modules directories
DirSize --path ~/projects --min-size 500MB --sort Size
```

### Cleanup Planning
```bash
# Find directories larger than 1GB for cleanup
DirSize --path /var --min-size 1GB --format Markdown --output cleanup-plan.md

# Detailed analysis with hidden files
DirSize --path ~ --handler Detailed --include-hidden --output home-analysis.json
```

## Integration

DirSize demonstrates several Ave.Extensions.FileSystem features:

- **IFileSystem**: File system abstraction for cross-platform compatibility
- **ITraversalHandler**: Custom handlers for processing filesystem entries
- **TraversalOptions**: Configurable traversal strategies and limits
- **FileSystemEntry**: Unified representation of files and directories
- **Error Handling**: Robust error handling with Result pattern

## Building and Running

```bash
# Build the application
dotnet build

# Run with default options
dotnet run

# Run with specific arguments
dotnet run -- --path /home/user --top 15 --format Json
```

## Dependencies

- **.NET 8.0**: Target framework
- **Ave.Extensions.FileSystem**: Core filesystem abstraction
- **Ave.Extensions.FileSystem.Local**: Local filesystem implementation
- **System.CommandLine**: Command-line argument parsing
- **Microsoft.Extensions.Hosting**: Dependency injection and hosting

## Related Samples

- **FileCounter**: Count files and directories with filtering
- **FileSearch**: Search for files with pattern matching

## License

This sample is part of the Ave.Extensions.FileSystem project and follows the same license terms.
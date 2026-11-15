# FileSearch

A cross-platform console application that demonstrates advanced file searching capabilities using the Ave.Extensions.FileSystem library. FileSearch showcases multi-criteria searching, different search strategies, various handler types, and multiple output formats while maintaining high performance through optimized traversal patterns.

## Features

### Search Criteria
- **Pattern matching**: Support for wildcards (`*`, `?`) and regular expressions
- **Size filtering**: Search for files larger or smaller than specified sizes (e.g., `1MB`, `500KB`)
- **Date filtering**: Filter by creation and modification dates
- **Attribute filtering**: Include/exclude hidden files
- **Case sensitivity**: Support for case-sensitive and case-insensitive searches
- **Depth control**: Limit traversal depth for performance optimization

### Handler Types
- **Simple**: Basic file information and path collection
- **Detailed**: Enhanced with file previews for text files
- **Metadata**: Complete file metadata including system attributes and content analysis

### Export Formats
- **Text**: Human-readable console output with statistics
- **JSON**: Structured data with full search criteria and results
- **CSV**: Tabular format suitable for spreadsheet applications
- **XML**: Hierarchical format with complete metadata
- **Markdown**: Documentation-friendly format with rich formatting

### Traversal Strategies
- **Auto**: Automatically selects the best strategy
- **Search**: Optimized for finding specific files quickly
- **FullScan**: Comprehensive traversal of all entries
- **MemoryEfficient**: Minimizes memory usage for large directories
- **Parallel**: Multi-threaded traversal for performance

## Usage Examples

### Basic Pattern Search
```bash
# Find all C# files
FileSearch --pattern "*.cs"

# Find all log files, case-sensitive
FileSearch --pattern "*.log" --case-sensitive
```

### Advanced Filtering
```bash
# Find large files modified recently
FileSearch --larger-than 10MB --modified-after 2024-01-01

# Find configuration files with regex
FileSearch --regex "config\.(json|xml|yml)$"

# Search with depth limit and parallel strategy
FileSearch --pattern "*.pdf" --max-depth 3 --strategy parallel
```

### Different Handlers and Formats
```bash
# Get detailed information with file previews
FileSearch --pattern "*.txt" --handler detailed

# Export metadata to JSON
FileSearch --pattern "*.exe" --handler metadata --format json --output executables.json

# Generate markdown report
FileSearch --larger-than 1GB --format markdown --output large-files.md
```

### Size and Date Filtering
```bash
# Find files between 1KB and 1MB
FileSearch --larger-than 1KB --smaller-than 1MB

# Find recently created files
FileSearch --created-after 2024-06-01 --max-results 50

# Include hidden files in search
FileSearch --pattern ".*" --include-hidden
```

## Command Line Options

| Option | Description | Default |
|--------|-------------|---------|
| `--path`, `-p` | Path to search in | Current directory |
| `--pattern`, `-n` | File name pattern with wildcards | `*` |
| `--regex`, `-r` | Regular expression for file names | - |
| `--modified-after`, `-ma` | Files modified after date | - |
| `--modified-before`, `-mb` | Files modified before date | - |
| `--created-after`, `-ca` | Files created after date | - |
| `--created-before`, `-cb` | Files created before date | - |
| `--larger-than`, `-l` | Files larger than size | - |
| `--smaller-than`, `-s` | Files smaller than size | - |
| `--max-results`, `-m` | Maximum number of results | 100 |
| `--include-hidden`, `-h` | Include hidden files | false |
| `--case-sensitive`, `-c` | Case-sensitive matching | false |
| `--max-depth` | Maximum traversal depth | unlimited |
| `--strategy` | Traversal strategy | Search |
| `--handler` | Handler type | Simple |
| `--format` | Output format | Text |
| `--output`, `-o` | Output file path | stdout |
| `--verbose`, `-v` | Show detailed errors | false |
| `--log-level` | Logging level | Warning |

## Size Format Examples

FileSearch supports various size formats:
- `1024` (bytes)
- `1KB`, `1MB`, `1GB`, `1TB`
- `1.5MB`, `2.5GB` (decimal values)

## Date Format

Dates should be provided in ISO format:
- `2024-06-08`
- `2024-06-08T10:30:00`

## Performance Tips

1. **Use specific patterns**: `*.cs` is faster than `*`
2. **Limit depth**: Use `--max-depth` for large directory trees
3. **Choose appropriate strategy**: 
   - Use `Search` for finding specific files
   - Use `Parallel` for large directories
   - Use `MemoryEfficient` for memory-constrained environments
4. **Set reasonable limits**: Use `--max-results` to prevent overwhelming output

## Architecture

FileSearch demonstrates advanced usage of the Ave.Extensions.FileSystem library:

- **Service-oriented design** with dependency injection
- **Configurable traversal strategies** for different use cases
- **Extensible handler pattern** for different output requirements
- **Multiple export formats** using the strategy pattern
- **Comprehensive error handling** with partial result support
- **Thread-safe operations** with proper synchronization

## Building and Running

```bash
# Build the application
dotnet build

# Run with default options
dotnet run

# Run with custom options
dotnet run -- --pattern "*.cs" --larger-than 1KB --format json
```

## See Also

- [FileCounter](../FileCounter/) - Count files and directories with detailed statistics
- [DirSize](../DirSize/) - Analyze directory sizes and generate comprehensive reports
- [Ave.Extensions.FileSystem](../../Ave.Extensions.FileSystem/) - Core file system library
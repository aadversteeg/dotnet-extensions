# ContentTypeAnalyzer

A cross-platform console application that demonstrates the MimeDetective integration with Ave.Extensions.FileSystem. It analyzes files based on their actual content using magic number detection, identifies misnamed files, and provides powerful content-based file organization capabilities.

## Features

- **Content Type Detection**: Analyze files by their actual content, not just extensions
- **Security Scanning**: Find misnamed or disguised files for security purposes  
- **File Organization**: Organize files by their true content type
- **Advanced Traversal**: Leverage the FileSystem extensions for efficient file system traversal

## Usage

### Basic Content Type Analysis
```bash
ContentTypeAnalyzer --path /downloads
```

### Find Misnamed Files
```bash
ContentTypeAnalyzer --path /documents --handler misnamed
```

### Filter by Category
```bash
ContentTypeAnalyzer --path /media --category image
```

### Advanced Analysis with High Confidence
```bash
ContentTypeAnalyzer --path /files --handler detailed --confidence 0.9
```

### Logging Examples
```bash
# Default quiet mode (Warning level only)
ContentTypeAnalyzer --path /documents

# Show informational messages
ContentTypeAnalyzer --path /documents --log-level Information

# Verbose debugging (equivalent to --verbose)
ContentTypeAnalyzer --path /documents --log-level Debug

# Very quiet mode (errors only)
ContentTypeAnalyzer --path /documents --log-level Error

# Override log level with verbose flag
ContentTypeAnalyzer --path /documents --log-level Error --verbose
```

## Command Line Options

- `--path, -p <path>`: Path to analyze (default: current directory)
- `--handler, -h <handler>`: Handler type: simple, misnamed, organize, detailed (default: simple)
- `--category, -c <category>`: Filter by category: image, document, archive, audio, video, text, application, data
- `--confidence <level>`: Minimum confidence level 0.0-1.0 (default: 0.7)
- `--definitions, -d <pack>`: Definition pack: default, condensed, exhaustive (default: default)
- `--usage <type>`: Usage type: personal, commercial (default: personal)
- `--strategy <strategy>`: Traversal strategy: Auto, Search, FullScan, MemoryEfficient, Parallel (default: Auto)
- `--output, -o <path>`: Output path for organize handler or export file
- `--export, -e <format>`: Export format: text, json, csv, xml
- `--max-bytes <bytes>`: Maximum bytes to read for detection (default: 8192)
- `--fallback-extension`: Use extension if content detection fails (default: true)
- `--max-depth`: Maximum directory depth to traverse
- `--verbose, -v`: Enable verbose logging (sets log level to Debug)
- `--log-level, -l <level>`: Set logging level: Trace, Debug, Information, Warning, Error, Critical (default: Warning)

## Handler Types

### Simple Handler
Provides basic content type analysis with statistics:
- Content type counts
- Category distribution  
- Detection confidence levels
- Total files and size

### Misnamed Files Handler
Identifies potentially misnamed files and assesses security risks:
- **High Risk**: Executable content with document extensions
- **Medium Risk**: Script content with document extensions  
- **Low Risk**: Simple format mismatches

## File Categories

The analyzer classifies files into these categories:
- **Image**: image/* MIME types (JPEG, PNG, GIF, etc.)
- **Document**: PDFs, Office docs, text files
- **Archive**: Compressed files (ZIP, RAR, 7z, etc.)
- **Audio**: audio/* MIME types (MP3, WAV, etc.)
- **Video**: video/* MIME types (MP4, AVI, etc.)
- **Text**: Plain text, code files, JSON, XML
- **Application**: Executables, libraries, scripts
- **Data**: Databases, data files
- **Unknown**: Unrecognized types

## Examples

### Security Scanning
```bash
# Find potentially malicious files
ContentTypeAnalyzer --path /downloads --handler misnamed

# Deep scan with verbose output
ContentTypeAnalyzer --path /quarantine --handler misnamed --verbose
```

### File Recovery
```bash
# Identify recovered files with no extensions
ContentTypeAnalyzer --path /recovered --handler simple

# Analyze specific file types
ContentTypeAnalyzer --path /recovered --category image
```

### Performance Testing
```bash
# Use parallel strategy for large directories
ContentTypeAnalyzer --path /large-dataset --strategy Parallel

# Memory-efficient traversal
ContentTypeAnalyzer --path /huge-directory --strategy MemoryEfficient
```

## Sample Output

### Simple Handler
```
Content Type Analysis Results
============================

Content Type Summary:
image/jpeg                    1,234 files
application/pdf                 456 files
video/mp4                       234 files
audio/mpeg                      789 files
text/plain                      123 files

Total: 2,836 files (15.3 GB)

Categories:
Image           1,456 files (51.3%)
Audio             789 files (27.8%)
Document          456 files (16.1%)
Video             234 files (8.3%)
Text              123 files (4.3%)

Detection Confidence:
High            2,456 files
Medium            289 files
Low                91 files
```

### Misnamed Files Handler
```
Found 12 potentially misnamed files:

HIGH RISK (2 files):
--------------------
/downloads/document.pdf
  Actual: application/x-msdownload
  Expected: .exe, .dll, .com
  Confidence: 1.00
  Risk: Executable content disguised as .pdf

MEDIUM RISK (3 files):
----------------------
/documents/report.doc
  Actual: text/html
  Expected: .html, .htm
  Confidence: 0.92

LOW RISK (7 files):
-------------------
/media/song.mp3
  Actual: audio/mp4
  Expected: .m4a, .aac
  Confidence: 0.98
```

## Integration with FileSystem Extensions

This sample demonstrates:
- **IFileSystem**: Core file system abstraction
- **IPathMapper**: Cross-platform path handling
- **TraversalOptions**: Configurable directory traversal
- **ContentTypeMapper**: MIME type detection integration
- **ITraversalHandler**: Custom processing logic

## Building and Running

```bash
# Navigate to the sample directory
cd samples/ContentTypeAnalyzer

# Build the application  
dotnet build

# Run with default options
dotnet run

# Run with custom options
dotnet run -- --path /some/directory --handler misnamed --verbose
```

## Dependencies

- Ave.Extensions.FileSystem
- Ave.Extensions.FileSystem.Local
- Ave.Extensions.FileSystem.MimeDetective
- System.CommandLine
- Microsoft.Extensions.Hosting
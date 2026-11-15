using System;
using System.IO;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.InMemory;
using Ave.Extensions.Functional;

namespace Benchmarks.Helpers;

public static class TestDataGenerator
{
    public static async Task<InMemoryFileSystem> CreateInMemoryFileSystemAsync(int fileCount)
    {
        var fileSystem = new InMemoryFileSystem();
        var root = CanonicalPath.Create("/test").Value;
        
        await fileSystem.CreateDirectoryAsync(root);
        
        var random = new Random(42); // Fixed seed for reproducible results
        
        for (int i = 0; i < fileCount; i++)
        {
            var dirDepth = random.Next(1, 4); // 1-3 levels deep
            var currentPath = root;
            
            // Create nested directory structure
            for (int depth = 0; depth < dirDepth; depth++)
            {
                var dirName = $"dir{depth}_{i % 10}";
                currentPath = currentPath.Append(dirName).Value;
                await fileSystem.CreateDirectoryAsync(currentPath);
            }
            
            // Create file
            var fileName = $"file{i:D6}.txt";
            var filePath = currentPath.Append(fileName).Value;
            var content = $"This is test file {i} with some content to make it realistic.";
            
            await fileSystem.WriteAllTextAsync(filePath, content);
        }
        
        return fileSystem;
    }
    
    public static async Task<string> CreateTempDirectoryWithFilesAsync(int fileCount)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        
        var random = new Random(42); // Fixed seed for reproducible results
        
        for (int i = 0; i < fileCount; i++)
        {
            var dirDepth = random.Next(1, 4); // 1-3 levels deep
            var currentPath = tempDir;
            
            // Create nested directory structure
            for (int depth = 0; depth < dirDepth; depth++)
            {
                var dirName = $"dir{depth}_{i % 10}";
                currentPath = Path.Combine(currentPath, dirName);
                Directory.CreateDirectory(currentPath);
            }
            
            // Create file
            var fileName = $"file{i:D6}.txt";
            var filePath = Path.Combine(currentPath, fileName);
            var content = $"This is test file {i} with some content to make it realistic.";
            
            await File.WriteAllTextAsync(filePath, content);
        }
        
        return tempDir;
    }
    
    public static void CleanupTempDirectory(string tempDir)
    {
        if (Directory.Exists(tempDir))
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors in benchmarks
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ave.Extensions.FileSystem;
using Ave.Extensions.FileSystem.Abstractions;
using Ave.Extensions.FileSystem.Models;
using Ave.Extensions.FileSystem.Traversal;
using Ave.Extensions.Functional;
using DirSize.Models;

namespace DirSize.Handlers;

public class TreeSizeHandler : SimpleSizeHandler
{
    private readonly Dictionary<string, TreeNode> _nodeMap = new();
    
    public TreeNode? RootNode { get; private set; }

    public override Task<Result<Unit, FileSystemError>> OnTraversalStartAsync(
        CanonicalPath rootPath, 
        CancellationToken cancellationToken = default)
    {
        var result = base.OnTraversalStartAsync(rootPath, cancellationToken);
        
        RootNode = new TreeNode 
        { 
            Name = Path.GetFileName(rootPath.Value) ?? rootPath.Value, 
            Path = rootPath.Value 
        };
        _nodeMap[rootPath.Value] = RootNode;
        
        return result;
    }

    public override Task<Result<TraversalControl, FileSystemError>> OnDirectoryEnterAsync(
        FileSystemEntry directory, 
        TraversalContext context, 
        CancellationToken cancellationToken = default)
    {
        var result = base.OnDirectoryEnterAsync(directory, context, cancellationToken);
        
        if (context.CurrentDepth > 0)
        {
            var node = new TreeNode
            {
                Name = Path.GetFileName(directory.Path.Value) ?? directory.Path.Value,
                Path = directory.Path.Value
            };

            _nodeMap[directory.Path.Value] = node;

            // Find parent and add as child
            var parentPath = Path.GetDirectoryName(directory.Path.Value);
            if (!string.IsNullOrEmpty(parentPath) && _nodeMap.TryGetValue(parentPath, out var parent))
            {
                node.Parent = parent;
                parent.Children.Add(node);
            }
        }
        
        return result;
    }

    public override Task<Result<Unit, FileSystemError>> OnTraversalCompleteAsync(
        TraversalSummary summary, 
        CancellationToken cancellationToken = default)
    {
        var result = base.OnTraversalCompleteAsync(summary, cancellationToken);
        
        // Update tree nodes with final size information
        foreach (var kvp in DirectorySizes)
        {
            if (_nodeMap.TryGetValue(kvp.Key, out var node))
            {
                node.Size = kvp.Value.TotalSize;
                node.FileCount = kvp.Value.FileCount;
            }
        }
        
        return result;
    }

    public void PrintTree(TreeNode? node = null, string indent = "", bool isLast = true)
    {
        node ??= RootNode;
        if (node == null) return;

        var connector = isLast ? "└── " : "├── ";
        var size = Services.SizeParser.FormatSize(node.Size);
        var files = node.FileCount > 0 ? $" ({node.FileCount} files)" : "";
        
        Console.WriteLine($"{indent}{connector}{node.Name} [{size}]{files}");
        
        var newIndent = indent + (isLast ? "    " : "│   ");
        var children = node.Children.OrderByDescending(c => c.Size).ToList();
        
        for (int i = 0; i < children.Count; i++)
        {
            PrintTree(children[i], newIndent, i == children.Count - 1);
        }
    }
}
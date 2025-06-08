using System.Collections.Generic;

namespace DirSize.Models;

public class TreeNode
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public int FileCount { get; set; }
    public List<TreeNode> Children { get; set; } = new();
    public TreeNode? Parent { get; set; }
}
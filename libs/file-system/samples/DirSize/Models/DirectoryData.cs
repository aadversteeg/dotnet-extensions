using System;

namespace DirSize.Models;

public class DirectoryData
{
    public long TotalSize { get; set; }
    public int FileCount { get; set; }
    public int SubdirectoryCount { get; set; }
    public DateTime LastModified { get; set; }
}
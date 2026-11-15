using System;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Specifies changes to watch for in a file system object.
    /// </summary>
    [Flags]
    public enum FileSystemNotifyFilters
    {
        /// <summary>
        /// No filters specified.
        /// </summary>
        None = 0,

        /// <summary>
        /// The name of the file or directory.
        /// </summary>
        FileName = 1,

        /// <summary>
        /// The directory name.
        /// </summary>
        DirectoryName = 2,

        /// <summary>
        /// File or directory attributes.
        /// </summary>
        Attributes = 4,

        /// <summary>
        /// The size of the file or directory.
        /// </summary>
        Size = 8,

        /// <summary>
        /// The last write time of the file or directory.
        /// </summary>
        LastWrite = 16,

        /// <summary>
        /// The last access time of the file or directory.
        /// </summary>
        LastAccess = 32,

        /// <summary>
        /// The creation time of the file or directory.
        /// </summary>
        CreationTime = 64,

        /// <summary>
        /// The security settings of the file or directory.
        /// </summary>
        Security = 256,
        
        /// <summary>
        /// All possible monitor filters.
        /// </summary>
        All = FileName | DirectoryName | Attributes | Size | LastWrite | LastAccess | CreationTime | Security
    }
}
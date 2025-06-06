namespace Ave.Extensions.FileSystem.Traversal
{
    /// <summary>
    /// Hints for optimizing traversal strategy.
    /// </summary>
    public enum TraversalStrategyHint
    {
        /// <summary>
        /// Automatically select the best strategy based on the context.
        /// </summary>
        Auto,
        
        /// <summary>
        /// Optimize for search operations with pattern matching.
        /// </summary>
        Search,
        
        /// <summary>
        /// Optimize for full directory scanning.
        /// </summary>
        FullScan,
        
        /// <summary>
        /// Optimize for memory efficiency with large directories.
        /// </summary>
        MemoryEfficient,
        
        /// <summary>
        /// Use parallel processing where possible.
        /// </summary>
        Parallel
    }
}
namespace Ave.Extensions.FileSystem.Traversal
{
    /// <summary>
    /// Options for file system traversal.
    /// </summary>
    public sealed class TraversalOptions
    {
        /// <summary>
        /// Gets the maximum depth to traverse.
        /// </summary>
        public int? MaxDepth { get; }
        
        
        
        /// <summary>
        /// Gets a value indicating whether traversal should be recursive.
        /// </summary>
        public bool Recursive { get; }
        
        /// <summary>
        /// Gets the strategy hint for optimization.
        /// </summary>
        public TraversalStrategyHint StrategyHint { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TraversalOptions"/> class.
        /// </summary>
        /// <param name="maxDepth">The maximum depth to traverse.</param>
        /// <param name="recursive">Whether traversal should be recursive.</param>
        /// <param name="strategyHint">The strategy hint for optimization.</param>
        public TraversalOptions(
            int? maxDepth = null,
            bool recursive = true,
            TraversalStrategyHint strategyHint = TraversalStrategyHint.Auto)
        {
            MaxDepth = maxDepth;
            Recursive = recursive;
            StrategyHint = strategyHint;
        }

        /// <summary>
        /// Creates a new options instance with the specified maximum depth.
        /// </summary>
        /// <param name="maxDepth">The maximum depth to traverse.</param>
        /// <returns>A new options instance.</returns>
        public TraversalOptions WithMaxDepth(int? maxDepth) =>
            new TraversalOptions(maxDepth, Recursive, StrategyHint);



        /// <summary>
        /// Creates a new options instance with the specified recursive setting.
        /// </summary>
        /// <param name="recursive">Whether traversal should be recursive.</param>
        /// <returns>A new options instance.</returns>
        public TraversalOptions WithRecursive(bool recursive) =>
            new TraversalOptions(MaxDepth, recursive, StrategyHint);

        /// <summary>
        /// Creates a new options instance with the specified strategy hint.
        /// </summary>
        /// <param name="strategyHint">The strategy hint for optimization.</param>
        /// <returns>A new options instance.</returns>
        public TraversalOptions WithStrategyHint(TraversalStrategyHint strategyHint) =>
            new TraversalOptions(MaxDepth, Recursive, strategyHint);

        /// <summary>
        /// Gets predefined options for top level only (non-recursive).
        /// </summary>
        public static TraversalOptions TopLevelOnly => new TraversalOptions(recursive: false);
    }
}
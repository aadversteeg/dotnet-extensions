namespace Ave.Extensions.FileSystem.Traversal
{
    /// <summary>
    /// Controls how traversal should proceed.
    /// </summary>
    public readonly struct TraversalControl
    {
        /// <summary>
        /// Gets the action to take for traversal flow control.
        /// </summary>
        public TraversalAction Action { get; }
        
        /// <summary>
        /// Gets the maximum depth to traverse from this point (optional).
        /// </summary>
        public int? MaxDepthFromHere { get; }
        
        /// <summary>
        /// Gets a value indicating whether children should be processed in parallel.
        /// </summary>
        public bool ProcessChildrenInParallel { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TraversalControl"/> struct.
        /// </summary>
        /// <param name="action">The action to take for traversal flow control.</param>
        /// <param name="maxDepthFromHere">The maximum depth to traverse from this point (optional).</param>
        /// <param name="processChildrenInParallel">Whether children should be processed in parallel.</param>
        public TraversalControl(
            TraversalAction action, 
            int? maxDepthFromHere = null, 
            bool processChildrenInParallel = false)
        {
            Action = action;
            MaxDepthFromHere = maxDepthFromHere;
            ProcessChildrenInParallel = processChildrenInParallel;
        }

        /// <summary>
        /// Gets a control that continues traversal normally.
        /// </summary>
        public static TraversalControl Continue => new TraversalControl(TraversalAction.Continue);
        
        /// <summary>
        /// Gets a control that skips this branch.
        /// </summary>
        public static TraversalControl Skip => new TraversalControl(TraversalAction.Skip);
        
        /// <summary>
        /// Gets a control that stops entire traversal.
        /// </summary>
        public static TraversalControl Stop => new TraversalControl(TraversalAction.Stop);
    }
}
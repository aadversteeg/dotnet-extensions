namespace Ave.Extensions.FileSystem.Traversal
{
    /// <summary>
    /// Defines actions for controlling traversal flow.
    /// </summary>
    public enum TraversalAction
    {
        /// <summary>
        /// Continue traversing normally.
        /// </summary>
        Continue,

        /// <summary>
        /// Skip this branch (don't traverse children).
        /// </summary>
        Skip,

        /// <summary>
        /// Stop entire traversal.
        /// </summary>
        Stop,

        /// <summary>
        /// Skip remaining items at current level.
        /// </summary>
        SkipSiblings
    }
}
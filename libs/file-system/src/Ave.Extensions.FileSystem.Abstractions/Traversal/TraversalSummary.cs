using System;

namespace Ave.Extensions.FileSystem.Traversal
{
    /// <summary>
    /// Summary of traversal operation.
    /// </summary>
    public readonly struct TraversalSummary
    {
        /// <summary>
        /// Gets the number of files processed during traversal.
        /// </summary>
        public long FilesProcessed { get; }
        
        /// <summary>
        /// Gets the number of directories processed during traversal.
        /// </summary>
        public long DirectoriesProcessed { get; }
        
        /// <summary>
        /// Gets the total bytes processed during traversal.
        /// </summary>
        public long TotalBytesProcessed { get; }
        
        /// <summary>
        /// Gets the duration of the traversal operation.
        /// </summary>
        public TimeSpan Duration { get; }
        
        /// <summary>
        /// Gets a value indicating whether the traversal was cancelled.
        /// </summary>
        public bool WasCancelled { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TraversalSummary"/> struct.
        /// </summary>
        /// <param name="filesProcessed">The number of files processed during traversal.</param>
        /// <param name="directoriesProcessed">The number of directories processed during traversal.</param>
        /// <param name="totalBytesProcessed">The total bytes processed during traversal.</param>
        /// <param name="duration">The duration of the traversal operation.</param>
        /// <param name="wasCancelled">Whether the traversal was cancelled.</param>
        public TraversalSummary(
            long filesProcessed,
            long directoriesProcessed,
            long totalBytesProcessed,
            TimeSpan duration,
            bool wasCancelled)
        {
            FilesProcessed = filesProcessed;
            DirectoriesProcessed = directoriesProcessed;
            TotalBytesProcessed = totalBytesProcessed;
            Duration = duration;
            WasCancelled = wasCancelled;
        }

        /// <summary>
        /// Gets an empty traversal summary.
        /// </summary>
        public static TraversalSummary Empty => new TraversalSummary(0, 0, 0, TimeSpan.Zero, false);

        /// <summary>
        /// Creates a cancelled traversal summary.
        /// </summary>
        /// <param name="filesProcessed">The number of files processed before cancellation.</param>
        /// <param name="directoriesProcessed">The number of directories processed before cancellation.</param>
        /// <param name="duration">The duration before cancellation.</param>
        /// <returns>A new cancelled traversal summary.</returns>
        public static TraversalSummary Cancelled(long filesProcessed, long directoriesProcessed, TimeSpan duration) =>
            new TraversalSummary(filesProcessed, directoriesProcessed, 0, duration, true);

        /// <summary>
        /// Returns a string representation of the traversal summary.
        /// </summary>
        /// <returns>A string representation of the traversal summary.</returns>
        public override string ToString() =>
            $"TraversalSummary: Files={FilesProcessed}, Directories={DirectoriesProcessed}, Bytes={TotalBytesProcessed}, Duration={Duration}, Cancelled={WasCancelled}";
    }
}
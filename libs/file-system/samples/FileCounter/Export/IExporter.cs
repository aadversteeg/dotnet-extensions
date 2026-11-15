using FileCounter.Handlers;

namespace FileCounter.Export
{
    /// <summary>
    /// Interface for exporting counting results in various formats.
    /// </summary>
    public interface IExporter
    {
        /// <summary>
        /// Exports the counting results to a string in the specified format.
        /// </summary>
        /// <param name="handler">The counting handler containing the results.</param>
        /// <returns>A string representation of the results in the exporter's format.</returns>
        string Export(ICountingHandler handler);

        /// <summary>
        /// Gets the file extension associated with this export format.
        /// </summary>
        string FileExtension { get; }

        /// <summary>
        /// Gets the MIME type associated with this export format.
        /// </summary>
        string MimeType { get; }
    }
}
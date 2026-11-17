using System;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Pdf;

namespace Ave.Extensions.Documents.Samples;

/// <summary>How the samples are written, shared by the samples app and its tests so both produce the same files.</summary>
public static class SampleOutput
{
    /// <summary>
    /// A fixed creation date (an input, so the files are byte-identical on every run) and the profile every sample is
    /// written to: PDF/A-3a and PDF/UA-1 at once — archivable, tagged and accessible.
    /// </summary>
    public static PdfRenderOptions Options { get; } = new(new DateTimeOffset(2026, 11, 15, 9, 22, 0, TimeSpan.FromHours(1)))
    {
        Profile = PdfProfile.PdfA3a.AndPdfUa1(),
    };
}

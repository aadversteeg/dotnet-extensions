namespace Ave.Documents.Pdf;

/// <summary>
/// The standard a PDF file is written to. A profile both ADDS what its standards require (XMP metadata, an output
/// intent, a structure tree, …) and makes the back-end REFUSE input they forbid (a figure without alternative text,
/// an attachment without a relationship), with an error that names the rule; it never weakens the document to make
/// it pass. A value: compare, store and combine profiles freely.
/// </summary>
/// <remarks>
/// <para>The profiles that make sense: <see cref="Plain"/>; <see cref="PdfA3b"/> (ISO 19005-3 level B, "visual
/// appearance preserved"); <see cref="PdfA3a"/> (level A: level B plus tagged, logical structure and Unicode text,
/// ISO 19005-3 §6.7–§6.8); <see cref="PdfUa1"/> (ISO 14289-1); and PDF/A-3 plus PDF/UA-1 with
/// <see cref="AndPdfUa1"/>, which is what a long-lived accessible business document wants.</para>
/// <para>PDF/A-3 here means a PDF 1.7 file (ISO 32000-1), the base ISO 19005-3 builds on; every rule cited is from
/// the veraPDF validation profiles (rule ids such as <c>6.8-3</c>), whose clause numbers are the standards'.</para>
/// </remarks>
public sealed record PdfProfile
{
    /// <summary>Plain PDF 1.7: no conformance claims, no structure tree, no XMP metadata.</summary>
    public static PdfProfile Plain { get; } = new();

    /// <summary>PDF/A-3 level B (ISO 19005-3): archivable, the appearance is preserved.</summary>
    public static PdfProfile PdfA3b { get; } = new() { PdfA3 = PdfA3Level.B };

    /// <summary>PDF/A-3 level A (ISO 19005-3): level B plus a logical structure (tagged PDF) and Unicode text.</summary>
    public static PdfProfile PdfA3a { get; } = new() { PdfA3 = PdfA3Level.A };

    /// <summary>PDF/UA-1 (ISO 14289-1): universally accessible, without an archival claim.</summary>
    public static PdfProfile PdfUa1 { get; } = new() { IsPdfUa1 = true };

    /// <summary>The PDF/A-3 conformance level claimed, or null for no PDF/A claim.</summary>
    public PdfA3Level? PdfA3 { get; init; }

    /// <summary>True when the file claims PDF/UA-1 conformance.</summary>
    public bool IsPdfUa1 { get; init; }

    /// <summary>True when the file is tagged PDF (ISO 32000-1 §14.8): PDF/A-3 level A and PDF/UA-1 both require it.</summary>
    public bool IsTagged => PdfA3 == PdfA3Level.A || IsPdfUa1;

    /// <summary>True when the file carries an XMP metadata stream: both standards identify themselves there (ISO 19005-3
    /// §6.6.4, ISO 14289-1 §5).</summary>
    public bool HasXmpMetadata => PdfA3 is not null || IsPdfUa1;

    /// <summary>True when the file needs an output intent that makes its device colours device-independent (ISO
    /// 19005-3 §6.2.4.3, veraPDF rule 6.2.4.3-2: DeviceRGB "shall only be used if … the file has a PDF/A OutputIntent
    /// that contains an RGB destination profile").</summary>
    public bool HasOutputIntent => PdfA3 is not null;

    /// <summary>This profile plus PDF/UA-1, for example <c>PdfProfile.PdfA3a.AndPdfUa1()</c>.</summary>
    /// <returns>The combined profile.</returns>
    public PdfProfile AndPdfUa1() => this with { IsPdfUa1 = true };

    /// <summary>The standards claimed, for example <c>PDF/A-3a + PDF/UA-1</c>, or <c>PDF</c> for plain.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => (PdfA3, IsPdfUa1) switch
    {
        (null, false) => "PDF",
        (null, true) => "PDF/UA-1",
        ({ } level, false) => $"PDF/A-3{level.ToString().ToLowerInvariant()}",
        ({ } level, true) => $"PDF/A-3{level.ToString().ToLowerInvariant()} + PDF/UA-1",
    };
}

/// <summary>The conformance levels of PDF/A-3 that the engine writes (ISO 19005-3 §2; level U is not offered:
/// level A includes everything it adds).</summary>
public enum PdfA3Level
{
    /// <summary>Level B, "basic": the visual appearance is preserved.</summary>
    B,

    /// <summary>Level A, "accessible": level B plus tagged PDF with a logical structure and Unicode text.</summary>
    A,
}

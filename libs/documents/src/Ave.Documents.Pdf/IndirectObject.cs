namespace Ave.Documents.Pdf;

/// <summary>
/// A numbered object in the file body, <c>12 0 obj … endobj</c> (ISO 32000-1 §7.3.10), that other objects
/// point at with a <see cref="PdfReference"/>. Holds either a direct object or a stream.
/// </summary>
public abstract record IndirectObject
{
    // Only the cases in this assembly exist, so the writer handles every one of them.
    private protected IndirectObject(int number) => Number = number;

    /// <summary>The object number, 1 or more.</summary>
    public int Number { get; }

    /// <summary>The reference that points at this object.</summary>
    public PdfReference Reference => new(Number);

    /// <summary>The direct object the writer checks before it writes (<see cref="PdfSerializer.Problem"/>): the value,
    /// or the stream's dictionary; null when the stream is missing.</summary>
    internal abstract PdfObject? Checked { get; }
}

/// <summary>An indirect object that holds a direct object, for example a page dictionary.</summary>
/// <param name="Number">The object number.</param>
/// <param name="Value">The object.</param>
public sealed record IndirectValue(int Number, PdfObject Value) : IndirectObject(Number)
{
    internal override PdfObject? Checked => Value;
}

/// <summary>An indirect object that holds a stream, for example a page's content stream.</summary>
/// <param name="Number">The object number.</param>
/// <param name="Stream">The stream.</param>
public sealed record IndirectStream(int Number, PdfStream Stream) : IndirectObject(Number)
{
    internal override PdfObject? Checked => Stream?.Dictionary;
}

using System;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Pdf;

namespace Ave.Extensions.Documents.Render.Pdf;

/// <summary>
/// The inputs of the PDF file that are not part of the document. Nothing here is read from the clock or the
/// machine, so the same document and options always give the same bytes. A value: options are equal exactly when they
/// write the same file — the same profile, identifier bytes and date, where the same date is the same instant in the same
/// offset, because the offset is written (D:20261116090000+01'00' is not D:20261116080000Z, though they are one instant).
/// </summary>
/// <param name="CreationDate">Written as the document's creation and modification date (ISO 32000-1 §14.3.3), in the
/// document information and in the XMP metadata.</param>
/// <param name="FileIdentifier">The file identifier (§14.4), not empty; null to derive 16 bytes from a hash of the
/// file's content.</param>
public sealed record PdfRenderOptions(DateTimeOffset CreationDate, ValueList<byte>? FileIdentifier = null)
{
    /// <summary>The standards the file is written to; <see cref="PdfProfile.Plain"/> unless set.</summary>
    public PdfProfile Profile { get; init; } = PdfProfile.Plain;

    /// <summary>The value of the /Producer entry: the software that wrote the file.</summary>
    public const string Producer = "Ave.Extensions.Documents";

    /// <summary>True when both options write the same file: every member is compared, the date with its offset
    /// (<see cref="DateTimeOffset.EqualsExact"/>), not only its instant as <see cref="DateTimeOffset"/>'s own equality
    /// does.</summary>
    /// <param name="other">The other options.</param>
    /// <returns>Whether they are equal.</returns>
    public bool Equals(PdfRenderOptions? other) =>
        other is not null && CreationDate.EqualsExact(other.CreationDate) && FileIdentifier == other.FileIdentifier && Profile == other.Profile;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(CreationDate.UtcTicks, CreationDate.Offset, FileIdentifier, Profile);
}

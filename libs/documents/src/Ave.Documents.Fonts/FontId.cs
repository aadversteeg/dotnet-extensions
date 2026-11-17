using System;
using System.Security.Cryptography;

namespace Ave.Documents.Fonts;

/// <summary>
/// Identifies a font by its content: the SHA-256 digest of the font file. The same bytes always give the same
/// id, wherever and however often they are loaded, so the PDF writer embeds each font once and the output does
/// not depend on load order or object identity.
/// </summary>
/// <param name="Digest">The SHA-256 digest of the font file, as lowercase hexadecimal.</param>
public readonly record struct FontId(string Digest)
{
    /// <summary>Computes the id of a font file.</summary>
    /// <param name="fontFile">The complete font file.</param>
    /// <returns>The id.</returns>
    public static FontId Of(ReadOnlySpan<byte> fontFile) => new(Convert.ToHexStringLower(SHA256.HashData(fontFile)));

    /// <summary>Returns the digest.</summary>
    /// <returns>The text form.</returns>
    public override string ToString() => Digest;
}

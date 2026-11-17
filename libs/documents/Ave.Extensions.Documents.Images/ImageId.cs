using System;
using System.Security.Cryptography;

namespace Ave.Extensions.Documents.Images;

/// <summary>
/// Identifies an image by its content: the SHA-256 digest of the file the caller passed in. The same bytes give
/// the same id however often they are loaded, so a back-end embeds each image once and output does not depend on
/// object identity (the same rule as <c>FontId</c>).
/// </summary>
/// <param name="Digest">The SHA-256 digest of the image file, as lowercase hexadecimal.</param>
public readonly record struct ImageId(string Digest)
{
    /// <summary>Computes the id of an image file.</summary>
    /// <param name="file">The complete image file.</param>
    /// <returns>The id.</returns>
    public static ImageId Of(ReadOnlySpan<byte> file) => new(Convert.ToHexStringLower(SHA256.HashData(file)));

    /// <summary>Returns the first 12 hex digits, enough to tell images apart in text output.</summary>
    /// <returns>The short text form.</returns>
    public override string ToString() => Digest[..12];
}

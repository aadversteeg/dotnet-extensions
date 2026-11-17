using System;
using System.Collections.Immutable;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Images;

namespace Ave.Extensions.Documents.Fluent;

/// <summary>
/// An image: a PNG or JPEG file given as bytes, like fonts are (the engine never reads files itself). Made by
/// <see cref="Elements.Image"/>. Without a size it shows at its natural size (one pixel = 0.75pt, the CSS pixel),
/// scaled down to the available width when wider; <see cref="Width"/> or <see cref="Height"/> alone keep the aspect
/// ratio; both together fit the image inside that box, still keeping the ratio.
/// </summary>
/// <param name="File">The image file.</param>
/// <param name="SpecifiedWidth">The width, or null.</param>
/// <param name="SpecifiedHeight">The height, or null.</param>
/// <param name="Format">What the modifiers set on the image's box.</param>
/// <param name="Style">The shared style applied with <see cref="With"/>.</param>
/// <param name="Source">Where the element was created.</param>
public sealed record ImageElement(ImageFile File, Length? SpecifiedWidth, Length? SpecifiedHeight, Style Format, Style Style, SourceLocation Source)
    : Element(Source), IBoxFormatted<ImageElement>
{
    /// <summary>What the image shows, in words, for readers who cannot see it; null when not given.</summary>
    public string? AlternativeText { get; init; }

    /// <summary>True when the image is decoration only and carries no information (it is then left out of the
    /// document structure, so a screen reader skips it).</summary>
    public bool IsDecorative { get; init; }

    /// <summary>
    /// Says what the image shows, in words (the alternative text a screen reader reads out, ISO 32000-1 §14.9.3).
    /// Describe its meaning, not its looks: "Logo of Versteeg IT Solutions", not "blue square".
    /// </summary>
    /// <param name="text">The text; not empty.</param>
    /// <returns>A copy with the alternative text.</returns>
    public ImageElement Alt(string text) => this with { AlternativeText = text };

    /// <summary>Marks the image as decoration that carries no information: a screen reader skips it.</summary>
    /// <returns>A copy marked decorative.</returns>
    public ImageElement Decorative() => this with { IsDecorative = true };

    /// <summary>Sets the image's width.</summary>
    /// <param name="width">A positive length.</param>
    /// <returns>A copy with the width.</returns>
    public ImageElement Width(Length width) => this with { SpecifiedWidth = width };

    /// <summary>Sets the image's height.</summary>
    /// <param name="height">A positive length.</param>
    /// <returns>A copy with the height.</returns>
    public ImageElement Height(Length height) => this with { SpecifiedHeight = height };

    /// <summary>Applies a shared style.</summary>
    /// <param name="style">The style; replaces a style applied before.</param>
    /// <returns>A copy with the style.</returns>
    public ImageElement With(Style style) => this with { Style = style };

    /// <inheritdoc/>
    ImageElement IFormatted<ImageElement>.WithFormat(Style format) => this with { Format = format };
}

/// <summary>
/// The bytes of an image file, compared by content: two files with the same bytes are equal, so documents that hold
/// the same image stay equal values. The bytes are copied once and never changed.
/// </summary>
public sealed class ImageFile : IEquatable<ImageFile>
{
    /// <summary>Copies the bytes of an image file.</summary>
    /// <param name="bytes">The file.</param>
    public ImageFile(ReadOnlySpan<byte> bytes)
    {
        Bytes = [.. bytes];
        Id = ImageId.Of(bytes);
    }

    /// <summary>The file.</summary>
    public ImmutableArray<byte> Bytes { get; }

    /// <summary>The content digest.</summary>
    public ImageId Id { get; }

    /// <summary>Equal when the bytes are equal.</summary>
    /// <param name="other">The other file.</param>
    /// <returns>Whether both hold the same bytes.</returns>
    public bool Equals(ImageFile? other) => other is not null && Id == other.Id;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ImageFile other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Id.GetHashCode();
}

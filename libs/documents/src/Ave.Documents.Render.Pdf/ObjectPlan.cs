using System;
using System.Collections.Immutable;
using System.Linq;

namespace Ave.Documents.Render.Pdf;

/// <summary>
/// The object numbers of the file, fixed in advance from the fonts, images, pages and what the profile adds, so every
/// object can refer to any other while it is built, and the numbering is the same on every run. Layout of the numbers:
/// catalog, page tree and document information first; then five objects per font; then one or two per image (the
/// image, and its soft mask when it has transparency); then two per page. After those, only when present: the XMP
/// metadata stream, the output intent's ICC profile, the structure tree root, the parent tree and one object per
/// structure element, and two objects per attachment. A plain file therefore numbers its objects exactly as before
/// conformance profiles existed.
/// </summary>
/// <param name="FontCount">How many fonts the document uses.</param>
/// <param name="ImageHasMask">For each image the document uses, whether it needs a soft mask.</param>
/// <param name="PageCount">How many pages it has.</param>
internal sealed record ObjectPlan(int FontCount, ImmutableArray<bool> ImageHasMask, int PageCount)
{
    public const int Catalog = 1;
    public const int PageTree = 2;
    public const int Info = 3;
    private const int FirstFontObject = 4;
    private const int ObjectsPerFont = 5;
    private const int ObjectsPerPage = 2;
    private const int ObjectsPerAttachment = 2;

    /// <summary>True when the file has an XMP metadata stream.</summary>
    public bool HasMetadata { get; init; }

    /// <summary>True when the file has an output intent (and so an ICC profile stream).</summary>
    public bool HasOutputIntent { get; init; }

    /// <summary>True when the file is tagged (structure tree root and parent tree).</summary>
    public bool IsTagged { get; init; }

    /// <summary>How many structure elements a tagged file has.</summary>
    public int StructureElementCount { get; init; }

    /// <summary>How many files are attached.</summary>
    public int AttachmentCount { get; init; }

    private int FirstImageObject => FirstFontObject + FontCount * ObjectsPerFont;

    private int FirstPageObject => FirstImageObject + ImageHasMask.Sum(hasMask => hasMask ? 2 : 1);

    private int AfterPages => FirstPageObject + PageCount * ObjectsPerPage;

    private int AfterMetadata => AfterPages + (HasMetadata ? 1 : 0);

    private int AfterOutputIntent => AfterMetadata + (HasOutputIntent ? 1 : 0);

    private int AfterStructure => AfterOutputIntent + (IsTagged ? 2 + StructureElementCount : 0);

    /// <summary>The total number of objects; they are numbered 1 to Count.</summary>
    public int Count => AfterStructure + AttachmentCount * ObjectsPerAttachment - 1;

    /// <summary>The numbers of the objects of the font at <paramref name="index"/> (0-based).</summary>
    public FontObjectNumbers Font(int index)
    {
        var first = FirstFontObject + index * ObjectsPerFont;
        return new FontObjectNumbers(Type0: first, CidFont: first + 1, Descriptor: first + 2, FontFile: first + 3, ToUnicode: first + 4);
    }

    /// <summary>The numbers of the objects of the image at <paramref name="index"/> (0-based).</summary>
    public ImageObjectNumbers Image(int index)
    {
        var first = FirstImageObject + ImageHasMask.Take(index).Sum(hasMask => hasMask ? 2 : 1);
        return new ImageObjectNumbers(first, ImageHasMask[index] ? first + 1 : null);
    }

    /// <summary>The numbers of the objects of the page at <paramref name="index"/> (0-based).</summary>
    public PageObjectNumbers Page(int index)
    {
        var first = FirstPageObject + index * ObjectsPerPage;
        return new PageObjectNumbers(Page: first, Contents: first + 1);
    }

    /// <summary>The metadata stream.</summary>
    public int Metadata => HasMetadata ? AfterPages : throw Absent("an XMP metadata stream");

    /// <summary>The output intent's ICC profile stream.</summary>
    public int OutputIntentProfile => HasOutputIntent ? AfterMetadata : throw Absent("an output intent");

    /// <summary>The structure tree root.</summary>
    public int StructureTreeRoot => IsTagged ? AfterOutputIntent : throw Absent("a structure tree");

    /// <summary>The parent tree of the structure tree.</summary>
    public int ParentTree => StructureTreeRoot + 1;

    /// <summary>The structure element at <paramref name="index"/> (0-based).</summary>
    public int StructureElement(int index) => StructureTreeRoot + 2 + index;

    /// <summary>The numbers of the objects of the attachment at <paramref name="index"/> (0-based).</summary>
    public AttachmentObjectNumbers Attachment(int index)
    {
        var first = AfterStructure + index * ObjectsPerAttachment;
        return new AttachmentObjectNumbers(FileSpecification: first, EmbeddedFile: first + 1);
    }

    /// <summary>Asking for the number of an object the plan does not have is a bug in the renderer.</summary>
    private static InvalidOperationException Absent(string what) => new($"The object plan has no {what}.");
}

/// <summary>The five objects that embed one font as a composite font.</summary>
internal sealed record FontObjectNumbers(int Type0, int CidFont, int Descriptor, int FontFile, int ToUnicode);

/// <summary>The image XObject and, for an image with transparency, its soft-mask image.</summary>
internal sealed record ImageObjectNumbers(int Image, int? SoftMask);

/// <summary>The page dictionary and its content stream.</summary>
internal sealed record PageObjectNumbers(int Page, int Contents);

/// <summary>The file specification and the embedded file stream of one attachment.</summary>
internal sealed record AttachmentObjectNumbers(int FileSpecification, int EmbeddedFile);

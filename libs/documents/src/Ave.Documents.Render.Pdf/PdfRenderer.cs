using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.Fonts;
using Ave.Documents.Images;
using Ave.Documents.PageModel;
using Ave.Documents.Pdf;
using Ave.Documents.Render.Pdf.Tagging;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.ErrorPaths.Functional;
using Ave.Extensions.Functional;

namespace Ave.Documents.Render.Pdf;

/// <summary>The PDF back-end: page model in, PDF file out.</summary>
public static class PdfRenderer
{
    /// <summary>
    /// Writes the page document as a PDF 1.7 file (ISO 32000-1) to the options' profile. Each font is embedded once, as
    /// a subset composite font with a ToUnicode map, so the text in the file can be selected, copied and searched; each
    /// image is embedded once, however often it is drawn. Deterministic: the same document and options give
    /// byte-identical output.
    /// </summary>
    /// <remarks>
    /// The steps: refuse what the profile forbids (<see cref="ConformanceRules"/>); read every page's markers into
    /// events (<see cref="PageEvents"/>); for a tagged profile, build the structure tree from them
    /// (<see cref="StructureTree"/>) and refuse a structure the profile forbids (an irregular table); then write every
    /// object.
    /// </remarks>
    /// <param name="document">The page document.</param>
    /// <param name="options">Creation date, file identifier and profile.</param>
    /// <returns>The PDF bytes, a <see cref="DocumentErrorCodes.Pdf.Conformance"/> error for input the profile forbids,
    /// or a <see cref="DocumentErrorCodes.Pdf.Write"/> error.</returns>
    public static Result<ImmutableArray<byte>, Error> Render(PageDocument document, PdfRenderOptions options) =>
        ConformanceRules.Check(document, options.Profile)
            .OnSuccessBind(checkedDocument => checkedDocument.Pages.Select((page, index) => (page, index))
                .TraverseAll(page => PageEvents.Read(page.page, page.index + 1)))
            .OnSuccessCombine(pages => options.Profile.IsTagged
                ? StructureTree.Build(pages).OnSuccessBind(tree => ConformanceRules.CheckStructure(tree, options.Profile)).OnSuccessMap(tree => (StructureTree?)tree)
                : Result<StructureTree?, Error>.Success(null))
            .OnSuccessBind((pages, tree) => Write(document, options, pages, tree));

    /// <summary>All objects of the file, in plan order, then the file itself.</summary>
    private static Result<ImmutableArray<byte>, Error> Write(
        PageDocument document, PdfRenderOptions options, IReadOnlyList<ImmutableArray<PageEvent>> pages, StructureTree? tree)
    {
        var profile = options.Profile;
        var attachments = document.Metadata.Attachments;
        var fonts = UsedFont.Collect(document);
        var images = UsedImage.Collect(document);
        var plan = new ObjectPlan(fonts.Length, [.. images.Select(image => image.HasMask)], document.Pages.Count)
        {
            HasMetadata = profile.HasXmpMetadata,
            HasOutputIntent = profile.HasOutputIntent,
            IsTagged = tree is not null,
            StructureElementCount = tree?.Elements.Length ?? 0,
            AttachmentCount = attachments.Count,
        };
        var resources = new ResourceNames(
            fonts.ToImmutableDictionary(used => used.Font),
            images.ToImmutableDictionary(used => used.Image));
        var marks = tree?.Marks ?? StructureTree.Unmarked(pages);

        var fontObjects = fonts.Select((used, index) => (used, index))
            .TraverseAll(font => FontEmbedding.Build(font.used, plan.Font(font.index)));
        var pageObjects = document.Pages.Select((page, index) => (page, index))
            .TraverseAll(page => PageObjects(page.page, pages[page.index], marks[page.index], plan, page.index, fonts, images, resources));

        return fontObjects
            .OnSuccessCombine(_ => pageObjects)
            .OnSuccessBind((fontParts, pageParts) => PdfWriter.Write(new PdfFile(
                [
                    new IndirectValue(ObjectPlan.Catalog, Catalog(document.Metadata, profile, plan)),
                    new IndirectValue(ObjectPlan.PageTree, PageTree(plan)),
                    new IndirectValue(ObjectPlan.Info, Info(document.Metadata, options)),
                    .. fontParts.SelectMany(parts => parts),
                    .. images.SelectMany((used, index) => ImageEmbedding.Build(used, plan.Image(index))),
                    .. pageParts.SelectMany(parts => parts),
                    .. plan.HasMetadata ? [Metadata(document.Metadata, options, plan)] : ImmutableArray<IndirectObject>.Empty,
                    .. plan.HasOutputIntent ? [OutputIntentProfile(plan)] : ImmutableArray<IndirectObject>.Empty,
                    .. tree is not null ? StructureObjects.Build(tree, plan) : [],
                    .. attachments.SelectMany((attachment, index) => AttachmentEmbedding.Build(attachment, plan.Attachment(index))),
                ],
                Catalog: new PdfReference(ObjectPlan.Catalog),
                Info: new PdfReference(ObjectPlan.Info),
                FileIdentifier: options.FileIdentifier)));
    }

    /// <summary>
    /// The document catalog (§7.7.2, Table 28): the root of the object tree. Besides the page tree, each entry only
    /// when it applies:
    /// <list type="bullet">
    /// <item><c>/Lang</c>, the document's language (§14.9.2);</item>
    /// <item><c>/Metadata</c>, the XMP stream (§14.3.2; ISO 19005-3 §6.6.2.1, ISO 14289-1 §7.1);</item>
    /// <item><c>/OutputIntents</c> (§14.11.5): one PDF/A output intent, subtype <c>/GTS_PDFA1</c> (the name every PDF/A
    /// part uses), with the sRGB profile as its destination (ISO 19005-3 §6.2.3);</item>
    /// <item><c>/MarkInfo &lt;&lt;/Marked true&gt;&gt;</c> and <c>/StructTreeRoot</c> for tagged PDF (§14.7.1, §14.8.1;
    /// veraPDF rules 6.7.2.2-1, 6.7.3.3-1, 6.2-1, 7.1-11);</item>
    /// <item><c>/ViewerPreferences &lt;&lt;/DisplayDocTitle true&gt;&gt;</c> for PDF/UA (§12.2, Table 150; rule 7.1-10),
    /// so a viewer shows the title, not the file name;</item>
    /// <item><c>/Names &lt;&lt;/EmbeddedFiles …&gt;&gt;</c> (§7.7.4) and <c>/AF</c>, the document's associated files
    /// (ISO 19005-3 §6.8 and Annex E; rule 6.8-4), for attachments.</item>
    /// </list>
    /// </summary>
    private static PdfDictionary Catalog(DocumentMetadata metadata, PdfProfile profile, ObjectPlan plan)
    {
        var fileSpecifications = Enumerable.Range(0, plan.AttachmentCount)
            .Select(index => (PdfObject)new PdfReference(plan.Attachment(index).FileSpecification))
            .ToImmutableArray();
        return PdfDictionary.Of(
            ("Type", new PdfName("Catalog")),
            ("Pages", new PdfReference(ObjectPlan.PageTree)),
            ("Lang", metadata.Language is { } language ? PdfString.FromText(language) : null),
            ("Metadata", plan.HasMetadata ? new PdfReference(plan.Metadata) : null),
            ("OutputIntents", plan.HasOutputIntent ? new PdfArray([OutputIntent(plan)]) : null),
            ("MarkInfo", plan.IsTagged ? PdfDictionary.Of(("Marked", new PdfBoolean(true))) : null),
            ("StructTreeRoot", plan.IsTagged ? new PdfReference(plan.StructureTreeRoot) : null),
            ("ViewerPreferences", profile.IsPdfUa1 ? PdfDictionary.Of(("DisplayDocTitle", new PdfBoolean(true))) : null),
            ("Names", fileSpecifications.IsEmpty ? null : PdfDictionary.Of(("EmbeddedFiles", PdfTrees.NameTree(
                metadata.Attachments.Select((attachment, index) => (AttachmentEmbedding.Key(attachment), fileSpecifications[index])))))),
            ("AF", fileSpecifications.IsEmpty ? null : new PdfArray(fileSpecifications)));
    }

    /// <summary>The output intent dictionary (§14.11.5, Table 365).</summary>
    private static PdfDictionary OutputIntent(ObjectPlan plan) => PdfDictionary.Of(
        ("Type", new PdfName("OutputIntent")),
        ("S", new PdfName("GTS_PDFA1")),
        ("OutputConditionIdentifier", PdfString.FromText(SrgbIccProfile.Description)),
        ("Info", PdfString.FromText(SrgbIccProfile.Description)),
        ("DestOutputProfile", new PdfReference(plan.OutputIntentProfile)));

    /// <summary>The ICC profile stream (§8.6.5.5, Table 66): <c>/N 3</c> colour components.</summary>
    private static IndirectObject OutputIntentProfile(ObjectPlan plan) =>
        new IndirectStream(plan.OutputIntentProfile, new PdfStream(PdfDictionary.Of(("N", new PdfInteger(3))), SrgbIccProfile.Bytes, StreamEncoding.Flate));

    /// <summary>
    /// The metadata stream (§14.3.2, Table 315): <c>/Type /Metadata /Subtype /XML</c> (veraPDF rule 6.6.2.1-1). It is
    /// written uncompressed, so that tools which search a file for its XMP packet without parsing the PDF find it.
    /// </summary>
    private static IndirectObject Metadata(DocumentMetadata metadata, PdfRenderOptions options, ObjectPlan plan) =>
        new IndirectStream(plan.Metadata, new PdfStream(
            PdfDictionary.Of(("Type", new PdfName("Metadata")), ("Subtype", new PdfName("XML"))),
            MetadataPacket.For(metadata, options).Write(),
            StreamEncoding.None));

    /// <summary>A flat page tree (§7.7.3.2, Table 29): one node whose kids are all the pages.</summary>
    private static PdfDictionary PageTree(ObjectPlan plan) => PdfDictionary.Of(
        ("Type", new PdfName("Pages")),
        ("Kids", new PdfArray([.. Enumerable.Range(0, plan.PageCount).Select(index => new PdfReference(plan.Page(index).Page))])),
        ("Count", new PdfInteger(plan.PageCount)));

    /// <summary>The document information dictionary (§14.3.3, Table 317). Absent title or author: entry left out. The
    /// same values go into the XMP metadata (<see cref="MetadataPacket"/>).</summary>
    private static PdfDictionary Info(DocumentMetadata metadata, PdfRenderOptions options) => PdfDictionary.Of(
        ("Title", metadata.Title is null ? null : PdfString.FromText(metadata.Title)),
        ("Author", metadata.Author is null ? null : PdfString.FromText(metadata.Author)),
        ("Producer", PdfString.FromText(PdfRenderOptions.Producer)),
        ("CreationDate", PdfDate.Format(options.CreationDate)),
        ("ModDate", PdfDate.Format(options.CreationDate)));

    /// <summary>
    /// A page object (§7.7.3.3, Table 30) and its content stream. The resources (§7.8.3, Table 33) name only the fonts
    /// and images this page uses. MediaBox is the page size in points with the origin at the bottom-left. In a tagged
    /// file, <c>/StructParents</c> is the page's key in the parent tree (§14.7.4.4) and <c>/Tabs /S</c> makes the tab
    /// order follow the structure (§12.5, Table 30; ISO 14289-1 §7.18.3).
    /// </summary>
    private static Result<ImmutableArray<IndirectObject>, Error> PageObjects(
        Page page,
        ImmutableArray<PageEvent> events,
        ImmutableArray<ContentMark> marks,
        ObjectPlan plan,
        int pageIndex,
        ImmutableArray<UsedFont> fonts,
        ImmutableArray<UsedImage> images,
        ResourceNames resources)
    {
        var numbers = plan.Page(pageIndex);
        var fontsHere = page.Items.OfType<GlyphRun>().Select(run => run.Font).ToHashSet();
        var imagesHere = page.Items.OfType<DrawImage>().Select(draw => draw.Image).ToHashSet();
        var fontResources = fonts
            .Select((used, index) => (used, index))
            .Where(font => fontsHere.Contains(font.used.Font))
            .Select(font => (font.used.ResourceName, (PdfObject?)new PdfReference(plan.Font(font.index).Type0)));
        var imageResources = images
            .Select((used, index) => (used, index))
            .Where(image => imagesHere.Contains(image.used.Image))
            .Select(image => (image.used.ResourceName, (PdfObject?)new PdfReference(plan.Image(image.index).Image)))
            .ToImmutableArray();

        return PageContentStream.Build(page, events, marks, resources).OnSuccessMap(content => ImmutableArray.Create<IndirectObject>(
            new IndirectValue(numbers.Page, PdfDictionary.Of(
                ("Type", new PdfName("Page")),
                ("Parent", new PdfReference(ObjectPlan.PageTree)),
                ("MediaBox", PdfArray.OfNumbers(0, 0, page.Size.Width, page.Size.Height)),
                ("Resources", PdfDictionary.Of(
                    ("Font", PdfDictionary.Of(fontResources)),
                    ("XObject", imageResources.IsEmpty ? null : PdfDictionary.Of(imageResources)))),
                ("Contents", new PdfReference(numbers.Contents)),
                ("StructParents", plan.IsTagged ? new PdfInteger(pageIndex) : null),
                ("Tabs", plan.IsTagged ? new PdfName("S") : null))),
            new IndirectStream(numbers.Contents, new PdfStream(PdfDictionary.Of(), content, StreamEncoding.Flate))));
    }
}

/// <summary>Every font and image of the document by its resource name, for the content streams.</summary>
/// <param name="Fonts">The used fonts.</param>
/// <param name="Images">The used images.</param>
internal sealed record ResourceNames(IReadOnlyDictionary<TrueTypeFont, UsedFont> Fonts, IReadOnlyDictionary<ImageData, UsedImage> Images);

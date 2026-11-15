using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.Fonts;
using Ave.Documents.Images;
using Ave.Documents.PageModel;
using Ave.Documents.Pdf;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Render.Pdf;

/// <summary>The PDF back-end: page model in, PDF file out.</summary>
public static class PdfRenderer
{
    /// <summary>
    /// Writes the page document as a PDF 1.7 file (ISO 32000-1). Each font is embedded once, as a subset composite
    /// font with a ToUnicode map, so the text in the file can be selected, copied and searched; each image is embedded
    /// once, however often it is drawn. Deterministic: the same document and options give byte-identical output.
    /// </summary>
    /// <param name="document">The page document.</param>
    /// <param name="options">Creation date and file identifier.</param>
    /// <returns>The PDF bytes, or a <see cref="DocumentErrorCodes.Pdf.Write"/> error.</returns>
    public static Result<ImmutableArray<byte>, Error> Render(PageDocument document, PdfRenderOptions options)
    {
        var fonts = UsedFont.Collect(document);
        var images = UsedImage.Collect(document);
        var plan = new ObjectPlan(fonts.Length, [.. images.Select(image => image.HasMask)], document.Pages.Count);
        var resources = new ResourceNames(
            fonts.ToImmutableDictionary(used => used.Font),
            images.ToImmutableDictionary(used => used.Image));

        var fontObjects = fonts.Select((used, index) => (used, index))
            .TraverseAll(font => FontEmbedding.Build(font.used, plan.Font(font.index)));
        var imageObjects = images.SelectMany((used, index) => ImageEmbedding.Build(used, plan.Image(index)));
        var pageObjects = document.Pages.Select((page, index) => (page, index))
            .TraverseAll(page => PageObjects(page.page, plan, page.index, fonts, images, resources));

        return fontObjects.OnSuccessBind(fontParts => pageObjects.OnSuccessBind(pageParts => PdfWriter.Write(new PdfFile(
            [
                new IndirectValue(ObjectPlan.Catalog, Catalog()),
                new IndirectValue(ObjectPlan.PageTree, PageTree(plan)),
                new IndirectValue(ObjectPlan.Info, Info(document.Info, options)),
                .. fontParts.SelectMany(parts => parts),
                .. imageObjects,
                .. pageParts.SelectMany(parts => parts),
            ],
            Catalog: new PdfReference(ObjectPlan.Catalog),
            Info: new PdfReference(ObjectPlan.Info),
            FileIdentifier: options.FileIdentifier))));
    }

    /// <summary>The document catalog (§7.7.2, Table 28): the root of the object tree.</summary>
    private static PdfDictionary Catalog() => PdfDictionary.Of(
        ("Type", new PdfName("Catalog")),
        ("Pages", new PdfReference(ObjectPlan.PageTree)));

    /// <summary>A flat page tree (§7.7.3.2, Table 29): one node whose kids are all the pages.</summary>
    private static PdfDictionary PageTree(ObjectPlan plan) => PdfDictionary.Of(
        ("Type", new PdfName("Pages")),
        ("Kids", new PdfArray([.. Enumerable.Range(0, plan.PageCount).Select(index => new PdfReference(plan.Page(index).Page))])),
        ("Count", new PdfInteger(plan.PageCount)));

    /// <summary>The document information dictionary (§14.3.3, Table 317). Absent title or author: entry left out.</summary>
    private static PdfDictionary Info(DocumentInfo info, PdfRenderOptions options) => PdfDictionary.Of(
        ("Title", info.Title is null ? null : PdfString.FromText(info.Title)),
        ("Author", info.Author is null ? null : PdfString.FromText(info.Author)),
        ("Producer", PdfString.FromText(PdfRenderOptions.Producer)),
        ("CreationDate", PdfDate.Format(options.CreationDate)),
        ("ModDate", PdfDate.Format(options.CreationDate)));

    /// <summary>
    /// A page object (§7.7.3.3, Table 30) and its content stream. The resources (§7.8.3, Table 33) name only the fonts
    /// and images this page uses. MediaBox is the page size in points with the origin at the bottom-left.
    /// </summary>
    private static Result<ImmutableArray<IndirectObject>, Error> PageObjects(
        Page page, ObjectPlan plan, int pageIndex, ImmutableArray<UsedFont> fonts, ImmutableArray<UsedImage> images, ResourceNames resources)
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

        return PageContentStream.Build(page, resources).OnSuccessMap(content => ImmutableArray.Create<IndirectObject>(
            new IndirectValue(numbers.Page, PdfDictionary.Of(
                ("Type", new PdfName("Page")),
                ("Parent", new PdfReference(ObjectPlan.PageTree)),
                ("MediaBox", PdfArray.OfNumbers(0, 0, page.Size.Width, page.Size.Height)),
                ("Resources", PdfDictionary.Of(
                    ("Font", PdfDictionary.Of(fontResources)),
                    ("XObject", imageResources.IsEmpty ? null : PdfDictionary.Of(imageResources)))),
                ("Contents", new PdfReference(numbers.Contents)))),
            new IndirectStream(numbers.Contents, new PdfStream(PdfDictionary.Of(), content, StreamEncoding.Flate))));
    }
}

/// <summary>Every font and image of the document by its resource name, for the content streams.</summary>
/// <param name="Fonts">The used fonts.</param>
/// <param name="Images">The used images.</param>
internal sealed record ResourceNames(IReadOnlyDictionary<TrueTypeFont, UsedFont> Fonts, IReadOnlyDictionary<ImageData, UsedImage> Images);

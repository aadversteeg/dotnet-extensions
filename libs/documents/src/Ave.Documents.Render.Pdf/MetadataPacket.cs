using System.Collections.Generic;
using System.Globalization;
using Ave.Documents.Core;
using Ave.Documents.Pdf.Xmp;

namespace Ave.Documents.Render.Pdf;

/// <summary>
/// The XMP metadata of the document (ISO 32000-1 §14.3.2), with which PDF/A and PDF/UA files identify the standards
/// they claim. It repeats what the document information dictionary says, with the same values, because readers may
/// consult either (ISO 32000-1 §14.3.3 lists the information entries; the XMP properties below are their documented
/// equivalents).
/// </summary>
internal static class MetadataPacket
{
    /// <summary>
    /// The properties, each only when its value exists:
    /// <list type="bullet">
    /// <item><c>dc:title</c> (Info /Title), <c>dc:creator</c> (Info /Author), <c>dc:language</c> (the catalog's /Lang);</item>
    /// <item><c>xmp:CreateDate</c> and <c>xmp:ModifyDate</c> (Info /CreationDate, /ModDate), <c>pdf:Producer</c> (Info /Producer);</item>
    /// <item><c>pdfaid:part</c> 3 and <c>pdfaid:conformance</c> A or B (ISO 19005-3 §6.6.4, veraPDF rules 6.6.4-1 to -3);</item>
    /// <item><c>pdfuaid:part</c> 1 (ISO 14289-1 §5, rules 5-1 and 5-2);</item>
    /// <item>for a file that claims both, the description of the PDF/UA identification schema, which PDF/A-3 does not
    /// predefine (<see cref="XmpSchemaDescription.PdfUaIdentification"/>).</item>
    /// </list>
    /// </summary>
    /// <param name="metadata">The document's metadata.</param>
    /// <param name="options">The dates and the profile.</param>
    /// <returns>The packet.</returns>
    public static XmpPacket For(DocumentMetadata metadata, PdfRenderOptions options) => new([.. Properties(metadata, options)]);

    private static IEnumerable<XmpProperty> Properties(DocumentMetadata metadata, PdfRenderOptions options)
    {
        var profile = options.Profile;
        if (metadata.Title is { } title)
        {
            yield return new XmpProperty(XmpNamespace.DublinCore, "title", new XmpLanguageAlternative(title));
        }

        if (metadata.Author is { } author)
        {
            yield return new XmpProperty(XmpNamespace.DublinCore, "creator", new XmpOrderedArray([new XmpText(author)]));
        }

        if (metadata.Language is { } language)
        {
            yield return new XmpProperty(XmpNamespace.DublinCore, "language", new XmpUnorderedArray([new XmpText(language)]));
        }

        yield return new XmpProperty(XmpNamespace.XmpBasic, "CreateDate", XmpDate.Format(options.CreationDate));
        yield return new XmpProperty(XmpNamespace.XmpBasic, "ModifyDate", XmpDate.Format(options.CreationDate));
        yield return new XmpProperty(XmpNamespace.AdobePdf, "Producer", new XmpText(PdfRenderOptions.Producer));

        if (profile.PdfA3 is { } level)
        {
            yield return new XmpProperty(XmpNamespace.PdfAIdentification, "part", new XmpText("3"));
            yield return new XmpProperty(XmpNamespace.PdfAIdentification, "conformance", new XmpText(level.ToString().ToUpper(CultureInfo.InvariantCulture)));
        }

        if (profile.IsPdfUa1)
        {
            yield return new XmpProperty(XmpNamespace.PdfUaIdentification, "part", new XmpText("1"));
        }

        if (profile.IsPdfUa1 && profile.PdfA3 is not null)
        {
            yield return XmpSchemaDescription.ContainerProperty([XmpSchemaDescription.PdfUaIdentification]);
        }
    }
}

using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;

namespace Ave.Extensions.Documents.Fluent;

/// <summary>
/// A whole document as the author wrote it. An immutable value: it can be stored, shared and asserted on in a
/// unit test without rendering anything — with <c>==</c> (children are held in a <see cref="ValueList{T}"/>, so
/// two documents with the same content are equal) or through the text form of its document model. Made by
/// <see cref="Elements.Document"/>; its text formatting is set with the <see cref="TextModifiers"/>, for
/// example <c>Document([...]).FontFamily("Lato").FontSize(11.Pt())</c>.
/// </summary>
/// <param name="Body">The content, top to bottom.</param>
/// <param name="Page">The page size and margins.</param>
/// <param name="Format">The document's text formatting (only its text part is used); must name a font family.</param>
/// <param name="Title">The title, or null.</param>
/// <param name="Author">The author, or null.</param>
/// <param name="Source">Where the document was created.</param>
public sealed record Document(
    ValueList<Element> Body,
    PageSetup Page,
    Style Format,
    string? Title,
    string? Author,
    SourceLocation Source) : IFormatted<Document>
{
    /// <summary>The document's language as a language tag (for example <c>nl-NL</c>), or null.</summary>
    public string? Language { get; init; }

    /// <summary>The files embedded in the document, in the order they were attached.</summary>
    public ValueList<Attachment> Attachments { get; init; }

    /// <summary>The layout policies of the document: <see cref="LayoutProfile.Default"/> unless changed with
    /// <see cref="MonolithicOverflow(DocumentModel.MonolithicOverflow)"/>, <see cref="EmptyRowParts(DocumentModel.EmptyRowParts)"/>,
    /// <see cref="FontMetricRounding(DocumentModel.FontMetricRounding)"/>, <see cref="FontSizeRounding(DocumentModel.FontSizeRounding)"/>,
    /// <see cref="LengthRounding(DocumentModel.LengthRounding)"/>, <see cref="SplitLineHalves(DocumentModel.SplitLineHalves)"/>,
    /// <see cref="TableEndOverflow(DocumentModel.TableEndOverflow)"/>, <see cref="BodyClip(DocumentModel.BodyClip)"/>,
    /// <see cref="CellsBesideSlices(DocumentModel.CellsBesideSlices)"/>,
    /// <see cref="RepeatedHeaderRows(DocumentModel.RepeatedHeaderRows)"/>, <see cref="SliceRestEnd(DocumentModel.SliceRestEnd)"/> or
    /// <see cref="CutRowAlignment(DocumentModel.CutRowAlignment)"/>.</summary>
    public LayoutProfile Profile { get; init; } = LayoutProfile.Default;

    /// <summary>
    /// What layout does with an image or a line taller than the page area: refuse the document (the default), or slice
    /// it over as many pages as it needs, as a browser prints it (<see cref="DocumentModel.MonolithicOverflow"/>).
    /// </summary>
    /// <param name="overflow">The policy.</param>
    /// <returns>A copy with the policy.</returns>
    public Document MonolithicOverflow(MonolithicOverflow overflow) => this with { Profile = Profile with { MonolithicOverflow = overflow } };

    /// <summary>
    /// Whether a table row may be cut where one part of it shows nothing: avoided where moving the row helps (the
    /// default), or allowed wherever its cells can break, as a browser prints it (<see cref="DocumentModel.EmptyRowParts"/>).
    /// </summary>
    /// <param name="parts">The policy.</param>
    /// <returns>A copy with the policy.</returns>
    public Document EmptyRowParts(EmptyRowParts parts) => this with { Profile = Profile with { EmptyRowParts = parts } };

    /// <summary>
    /// Whether the fonts' ascent, descent and line gap are used exactly (the default) or each rounded to a whole CSS
    /// pixel as a browser (Chromium) does, which makes a line of <c>line-height: normal</c> a whole number of pixels
    /// (<see cref="DocumentModel.FontMetricRounding"/>).
    /// </summary>
    /// <param name="rounding">The policy.</param>
    /// <returns>A copy with the policy.</returns>
    public Document FontMetricRounding(FontMetricRounding rounding) => this with { Profile = Profile with { FontMetricRounding = rounding } };

    /// <summary>
    /// Whether text is shaped at its font size as it is given (the default) or at the two sizes a browser (Chromium) rounds
    /// it to: floored to 1/100 CSS pixel for kerning and mark positions, and further truncated to 1/64 pixel for the
    /// glyphs' advances, which makes text at a size that is not a whole number of 1/64 pixels a little narrower
    /// (<see cref="DocumentModel.FontSizeRounding"/>).
    /// </summary>
    /// <param name="rounding">The policy.</param>
    /// <returns>A copy with the policy.</returns>
    public Document FontSizeRounding(FontSizeRounding rounding) => this with { Profile = Profile with { FontSizeRounding = rounding } };

    /// <summary>
    /// Whether lengths are kept as layout computes them (the default) or put on a browser's (Chromium's) layout unit of
    /// 1/64 CSS pixel where it puts them there: today the width of each text item, rounded up, which moves the text after
    /// it and can wrap a line a little earlier (<see cref="DocumentModel.LengthRounding"/>).
    /// </summary>
    /// <param name="rounding">The policy.</param>
    /// <returns>A copy with the policy.</returns>
    public Document LengthRounding(LengthRounding rounding) => this with { Profile = Profile with { LengthRounding = rounding } };

    /// <summary>
    /// How thick each half of a table's grid line is drawn where a page break between rows splits the line
    /// (<see cref="TableElement.SplitLinesAtBreaks"/>): exactly half the line (the default), or at least one CSS pixel,
    /// as a browser (Chromium) prints it (<see cref="DocumentModel.SplitLineHalves"/>). Layout does not change.
    /// </summary>
    /// <param name="halves">The policy.</param>
    /// <returns>A copy with the policy.</returns>
    public Document SplitLineHalves(SplitLineHalves halves) => this with { Profile = Profile with { SplitLineHalves = halves } };

    /// <summary>
    /// Where a grid line that passes the page end below a table row goes: the table ends on the page (the default),
    /// or what passed continues on the next page and takes its height there, as a browser (Chromium) prints it
    /// (<see cref="DocumentModel.TableEndOverflow"/>).
    /// </summary>
    /// <param name="overflow">The policy.</param>
    /// <returns>A copy with the policy.</returns>
    public Document TableEndOverflow(TableEndOverflow overflow) => this with { Profile = Profile with { TableEndOverflow = overflow } };

    /// <summary>
    /// Whether what a page's body draws past its area shows: it does (the default), or the body is clipped to its area,
    /// the page area less the header and footer areas, as a browser (Chromium) prints it (<see cref="DocumentModel.BodyClip"/>).
    /// The pages and the text do not change.
    /// </summary>
    /// <param name="clip">The policy.</param>
    /// <returns>A copy with the policy.</returns>
    public Document BodyClip(BodyClip clip) => this with { Profile = Profile with { BodyClip = clip } };

    /// <summary>
    /// Where the other cells of a table row go on, on the pages that go on with the slices of a piece taller than the
    /// page in one of its cells: beside the slices from the top of each page (the default), or below the slice each page
    /// shows, as a browser (Chromium) prints it (<see cref="DocumentModel.CellsBesideSlices"/>).
    /// </summary>
    /// <param name="cells">The policy.</param>
    /// <returns>A copy with the policy.</returns>
    public Document CellsBesideSlices(CellsBesideSlices cells) => this with { Profile = Profile with { CellsBesideSlices = cells } };

    /// <summary>
    /// Where the content of a cell aligned middle or bottom goes on the later pages of a table row that a page break cuts:
    /// aligned in the rest of its row (the default), or at the top of its part on every page, as a browser (Chromium)
    /// prints it (<see cref="DocumentModel.CutRowAlignment"/>).
    /// </summary>
    /// <param name="alignment">The policy.</param>
    /// <returns>A copy with the policy.</returns>
    public Document CutRowAlignment(CutRowAlignment alignment) => this with { Profile = Profile with { CutRowAlignment = alignment } };

    /// <summary>
    /// On which later pages of a table its repeated header rows are drawn, where a piece taller than the room below them
    /// is sliced in one of its cells: above every slice (the default), or on every page where the table still lays
    /// content out and not on a page that only shows the rest of a piece, as a browser (Chromium) prints it
    /// (<see cref="DocumentModel.RepeatedHeaderRows"/>).
    /// </summary>
    /// <param name="rows">The policy.</param>
    /// <returns>A copy with the policy.</returns>
    public Document RepeatedHeaderRows(RepeatedHeaderRows rows) => this with { Profile = Profile with { RepeatedHeaderRows = rows } };

    /// <summary>
    /// Where the part of a table cell ends that holds nothing but the rest of a piece sliced on an earlier page: below its
    /// half of the grid line below it (the default), or at the rest's end, the line centred on it, as a browser (Chromium)
    /// prints it (<see cref="DocumentModel.SliceRestEnd"/>).
    /// </summary>
    /// <param name="end">The policy.</param>
    /// <returns>A copy with the policy.</returns>
    public Document SliceRestEnd(SliceRestEnd end) => this with { Profile = Profile with { SliceRestEnd = end } };

    /// <summary>
    /// Embeds a file in the document, for example its data as XML. Made with <see cref="Elements.Attachment"/>;
    /// PDF/A-3 also needs its relationship to the document (<see cref="AttachmentModifiers.WithRelationship"/>).
    /// </summary>
    /// <param name="attachment">The file.</param>
    /// <returns>A copy with the file attached after the ones attached before.</returns>
    public Document Attach(Attachment attachment) => this with { Attachments = [.. Attachments, attachment] };

    /// <inheritdoc/>
    Document IFormatted<Document>.WithFormat(Style format) => this with { Format = format };
}

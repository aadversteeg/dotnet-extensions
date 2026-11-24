using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Typesetting;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Layout;

/// <summary>Builds line boxes from broken lines (CSS 2.1 §10.8 "Line height calculations").</summary>
internal static class LineBoxes
{
    /// <summary>An item of a paragraph's lines: an inline run with its glyphs (<see cref="ShapedRun"/>) or an atomic inline
    /// (<see cref="AtomicInline"/>).</summary>
    public abstract record Item
    {
        /// <summary>The inline groups the item lies in, outermost first, each with its font resolved; empty outside every
        /// group (<see cref="InlineGroup"/>, which layout reads through: <c>InlineLayout.Leaves</c>).</summary>
        public ValueList<Group> Groups { get; init; } = [];
    }

    /// <summary>An inline group with its own font, which gives the height of its area on each line.</summary>
    /// <param name="Node">The group.</param>
    /// <param name="Font">The font its text properties resolve to.</param>
    public sealed record Group(InlineGroup Node, TrueTypeFont Font);

    /// <summary>An inline run with its resolved font and shaped glyphs.</summary>
    public sealed record ShapedRun(InlineRun Run, TrueTypeFont Font, ValueList<ShapedGlyph> Glyphs) : Item
    {
        /// <summary>The run's decorations with their decorating fonts resolved, in order (<see cref="TextStyle.Decorations"/>);
        /// empty until layout resolves them.</summary>
        public ValueList<TextDecorations.Resolved> Decorations { get; init; } = [];
    }

    /// <summary>
    /// An atomic inline (CSS Display 3, Appendix A): a child box of the paragraph laid out by its own algorithm, one item of
    /// the line that is never broken. On the line it is its margin box (CSS 2.1 §10.8: "For replaced elements, inline-block
    /// elements, and inline-table elements, this is the height of their margin box").
    /// </summary>
    /// <param name="Box">The child box.</param>
    /// <param name="Fragment">Its fragment, once laid out at the paragraph's width; null while only measured.</param>
    /// <param name="Width">The width of its margin box, in points: its advance on the line.</param>
    public sealed record AtomicInline(Box Box, BoxFragment? Fragment, double Width) : Item
    {
        /// <summary>Its horizontal margins, in points.</summary>
        public double Margins => Box.Style.Margin.Horizontal.Points;
    }

    /// <summary>
    /// An edge of an inline group's box (<see cref="InlineGroup.Box"/>): the room its margin, border and padding take on one
    /// side, an item of the line of its own that the line breaker keeps with the group's first or last character
    /// (<see cref="ShapedGlyph.Edge"/>). It draws nothing and has no height: the group's part on the line paints the box.
    /// </summary>
    /// <param name="Group">The group.</param>
    /// <param name="Side">Which edge.</param>
    /// <param name="Width">The room it takes, in points.</param>
    public sealed record Edge(InlineGroup Group, InlineBoxEdge Side, double Width) : Item;

    /// <summary>The glyphs an item puts on the line breaker's runs: a run's own glyphs, for an atomic inline one glyph
    /// as wide as its margin box (<see cref="ShapedGlyph.AtomicInline"/>), for an edge of an inline box its stand-in
    /// (<see cref="ShapedGlyph.BoxEdge"/>).</summary>
    /// <param name="item">The item.</param>
    /// <returns>Its glyphs.</returns>
    public static ValueList<ShapedGlyph> GlyphsOf(Item item) => item switch
    {
        ShapedRun run => run.Glyphs,
        AtomicInline atomic => [ShapedGlyph.AtomicInline(atomic.Width)],
        Edge edge => [ShapedGlyph.BoxEdge(edge.Width, edge.Side)],
        _ => throw new InvalidOperationException($"Unknown line item {item.GetType().Name}."),
    };

    /// <summary>The strut of a paragraph's lines (CSS 2.1 §10.8.1): the paragraph's own font, resolved, and its text
    /// properties (font size and line height).</summary>
    /// <param name="Font">The paragraph's font.</param>
    /// <param name="Style">The paragraph's computed text properties.</param>
    public sealed record Strut(TrueTypeFont Font, TextStyle Style);

    /// <summary>
    /// The vertical extent of one inline box around the baseline (CSS 2.1 §10.8.1, "Leading and half-leading"; CSS
    /// Inline 3 §5.3). A is the font's ascent and D its descent, from its line metrics (<see cref="Fonts.FontMetrics.Line"/>:
    /// 'OS/2' typographic or 'hhea', as Chromium chooses), at the box's font size; the leading L is added
    /// half above A and half below D. With <c>line-height: normal</c> (the initial value) L is the font's line gap, so
    /// the box is ascent + descent + line gap high (Inline 3 §5.3: "the font's line gap metric may also be incorporated
    /// into A and D by adding half to each side as half-leading"); with any other value "L = 'line-height' - AD" (CSS 2.1
    /// §10.8.1), so the box is exactly the line height high ("Note. L may be negative."). Under
    /// <see cref="FontMetricRounding.WholePixels"/> A, D and the line gap are each rounded to whole pixels and the half
    /// above is floored to a whole pixel, the rest of L going below, as Chromium does.
    /// </summary>
    internal sealed record InlineExtent(double Ascent, double Descent, double LeadingAbove, double LeadingBelow)
    {
        /// <summary>How far the box's baseline lies above the line's baseline (<see cref="TextStyle.BaselineShift"/>).</summary>
        public double Shift { get; init; }

        public double Above => Ascent + LeadingAbove + Shift;
        public double Below => Descent + LeadingBelow - Shift;

        public static InlineExtent Of(TrueTypeFont font, double fontSize, LineHeight lineHeight, FontMetricRounding rounding)
        {
            var whole = rounding == FontMetricRounding.WholePixels;
            double Metric(int designUnits) =>
                whole ? CssPixels.Round(font.Metrics.ToPoints(designUnits, fontSize)) : font.Metrics.ToPoints(designUnits, fontSize);
            var metrics = font.Metrics.Line;
            var ascent = Metric(metrics.Ascender);
            var descent = Metric(-metrics.Descender);
            var leading = lineHeight.HeightFor(fontSize) is { } height ? height - (ascent + descent) : Metric(metrics.LineGap);
            var above = whole ? CssPixels.Floor(leading / 2) : leading / 2;
            return new InlineExtent(ascent, descent, above, leading - above);
        }

        /// <summary>The extent of a run's inline box, shifted by the run's <see cref="TextStyle.BaselineShift"/> (<see cref="Build"/>,
        /// THE RULE FOR A SHIFTED RUN ON THE LINE).</summary>
        public static InlineExtent OfRun(ShapedRun run, FontMetricRounding rounding) =>
            Of(run.Font, run.Run.Style.FontSize.Points, run.Run.Style.LineHeight, rounding) with { Shift = run.Run.Style.BaselineShift.Points };

        /// <summary>The extent of an atomic inline's margin box: all of it above the baseline (<see cref="Build"/>, THE RULE
        /// FOR AN ATOMIC INLINE ON THE LINE).</summary>
        public static InlineExtent OfMarginBox(AtomicInline atomic) =>
            new(atomic.Box.Style.Margin.Vertical.Points + (atomic.Fragment?.Size.Height ?? 0), 0, 0, 0);
    }

    /// <summary>
    /// Builds the line box for one line. All inline boxes sit on one baseline (<c>vertical-align: baseline</c>);
    /// the line box reaches from the highest top to the lowest bottom among them (CSS 2.1 §10.8: "the line box
    /// height is the distance between the uppermost box top and the lowermost box bottom. (This includes the strut,
    /// as explained under 'line-height' below.)"): every line starts with the paragraph's strut, a box of no width with
    /// the paragraph's own font and line height, so no line is lower than the paragraph's line height.
    /// </summary>
    /// <remarks>
    /// Horizontally the line is placed by its paragraph's <c>text-align</c> (<see cref="LineJustification.Place"/>):
    /// moved right or to the centre, or justified by widening its word separators; an atomic inline is none (case
    /// <c>ii-align</c>: in "one", image, "two" the justified line widens only the space after "two").
    /// <para>THE RULE FOR AN ATOMIC INLINE ON THE LINE: its margin box sits with its bottom on the baseline and reaches as
    /// high above it as it is tall (CSS 2.1 §10.8, the margin box; §10.8.1, 'vertical-align': "baseline: Align the baseline
    /// of the box with the baseline of the parent box. If the box does not have a baseline, align the bottom margin edge
    /// with the parent's baseline"), as Chromium synthesizes the baseline of an image off its margin box's bottom edge (Blink
    /// <c>LogicalBoxFragment::BaselineMetrics</c>, core/layout/logical_box_fragment.cc). So it makes the line taller when it
    /// is taller than the strut's part above the baseline, never below it; its margins count above, below and beside it
    /// (case <c>ii-baseline</c>).</para>
    /// <para>THE RULE FOR A SHIFTED RUN ON THE LINE: a run with a <see cref="TextStyle.BaselineShift"/> s has its own baseline s
    /// above the line's baseline (CSS 2.1 §10.8.1, <c>vertical-align</c>: the box is raised or lowered), and its inline box,
    /// the same extent as unshifted (its ascent and descent, and under a line height other than <c>normal</c> its
    /// half-leadings), moves with it: it reaches s further above the baseline and s less below it. The line box reaches from
    /// the highest top to the lowest bottom of all its boxes, the strut's among them, as for runs that are not shifted. So a
    /// raised run makes the line taller above its baseline, a lowered one below it, and only by what its own box passes the
    /// others'. Measured in Chromium 159 under a line height of 18pt (cases <c>va-shift</c>, <c>va-length</c>: 10.5 pt text
    /// raised 5.656 px is a line of 17 + 5.656 px above the baseline and 7 px below, 22.242 pt, where the strut alone is
    /// 18 pt; lowered 3.797 px, 17 px above and 10.797 px below) and under <c>normal</c> (cases <c>va-shift</c>,
    /// <c>va-elements</c>, <c>va-sizes</c>: 14 + 5.656 px above and 3 px below; a smaller sup's own box of 12 + 2 px raised
    /// 5.656 px reaches 17.656 px above). The run's glyphs and its decorations are drawn on its own baseline
    /// (<see cref="TextDecorations"/>: the underline of a <c>sup</c> lies under the raised text, case <c>va-decorated</c>).
    /// Its place on the line, its width and every break stay as they are (case <c>va-decorated</c>: a raised span at a
    /// line's start and across a soft wrap is raised on both lines).</para>
    /// </remarks>
    /// <param name="broken">The broken line.</param>
    /// <param name="runs">The paragraph's runs; <see cref="LineSegment.Run"/> indexes into it.</param>
    /// <param name="width">The width of the line box (the paragraph's content width).</param>
    /// <param name="align">The paragraph's text-align.</param>
    /// <param name="isLastLine">True for the paragraph's last line, which is never justified.</param>
    /// <param name="strut">The paragraph's strut, or null when the model gives the paragraph no text properties.</param>
    /// <param name="rounding">How the fonts' metrics are taken (the document's profile).</param>
    /// <para>A run's decorations are filled rectangles (<see cref="RuleFragment"/>) among the line's children, around the piece
    /// they decorate in painting order (<see cref="TextDecorations"/>, THE RULE FOR TEXT DECORATION LINES); they change neither
    /// the line's height nor its width.</para>
    /// <param name="empty">The paragraph's runs that put no glyph on any line and lie on this one (<see cref="EmptyRunsByLine"/>):
    /// their inline boxes count for the line's height all the same.</param>
    /// <param name="outlines">The glyph outlines decoded for the paragraph, for the gaps a decoration leaves around ink.</param>
    /// <returns>The line fragment with its text fragments, or the error of a glyph outline a decoration cannot read.</returns>
    public static Result<LineFragment, Error> Build(
        TextLine broken, IReadOnlyList<Item> runs, double width, TextAlign align, bool isLastLine, Strut? strut, FontMetricRounding rounding, InkOutlines outlines, IReadOnlyList<ShapedRun>? empty = null)
    {
        var (line, offset) = LineJustification.Place(broken, width, align, isLastLine);
        var pieces = line.Segments
            .Select(segment => (Segment: segment, Run: runs[segment.Run]))
            .Select(piece => (piece.Segment, piece.Run, Extent: piece.Run switch
            {
                ShapedRun run => InlineExtent.OfRun(run, rounding),
                AtomicInline atomic => InlineExtent.OfMarginBox(atomic),

                // An edge of an inline box has no height of its own (THE INLINE BOX STYLE).
                Edge => null,
                _ => throw new InvalidOperationException($"Unknown line item {piece.Run.GetType().Name}."),
            }))
            .ToImmutableArray();
        ImmutableArray<InlineExtent> extents = [.. pieces.Where(piece => piece.Extent is not null).Select(piece => piece.Extent!),
            .. (empty ?? []).Select(run => InlineExtent.OfRun(run, rounding)),
            .. strut is { } own ? [InlineExtent.Of(own.Font, own.Style.FontSize.Points, own.Style.LineHeight, rounding)] : Array.Empty<InlineExtent>()];
        if (pieces.Length > 0 && pieces.All(piece => piece.Run is Edge))
        {
            // A line of empty inline boxes alone (InlineLayout.OnlyEdges): "Empty inline elements generate empty inline boxes,
            // but these boxes still have margins, padding, borders and a line height, and thus influence the line box height
            // calculation" (CSS 2.1 §10.8). Measured only with the paragraph's own font (case ib-empty-alone), where the strut
            // gives the line its height; a paragraph without text properties has no strut and takes the boxes' own.
            var boxes = pieces.Select(piece => piece.Run.Groups[^1]).Select(group => (group.Node, Extent: InlineExtent.Of(group.Font, group.Node.Style.FontSize.Points, group.Node.Style.LineHeight, rounding))).ToList();
            if (extents.Length == 0)
            {
                extents = [.. boxes.Select(box => box.Extent)];
            }
            else if (boxes.FirstOrDefault(box => box.Extent.Above > extents.Max(extent => extent.Above) + 1e-9 || box.Extent.Below > extents.Max(extent => extent.Below) + 1e-9) is { Node: { } taller })
            {
                return Result<LineFragment, Error>.Failure(LayoutErrors.InlineBox(taller, "an empty inline box alone on a line, with a font or line height that reaches above or below the paragraph's, is not drawn in this version; give it the paragraph's font size, or put text beside it"));
            }
        }


        var above = extents.Max(extent => extent.Above);
        var below = extents.Max(extent => extent.Below);
        var children = ImmutableArray.CreateBuilder<PlacedFragment>(pieces.Length);
        var groups = new LineGroups(above, rounding);
        var x = offset;
        foreach (var (segment, item, extent) in pieces)
        {
            var firstChild = children.Count;
            if (item is Edge edge)
            {
                // The edge's margin lies outside the box: the part's area starts after the start margin and ends before the end
                // margin.
                var box = edge.Group.Box!;
                var (left, right) = edge.Side == InlineBoxEdge.Start ? (x + box.MarginLeft.Points, x + segment.Width) : (x, x + segment.Width - box.MarginRight.Points);
                groups.Piece(item.Groups, firstChild, firstChild, left, right);
                groups.Edge(edge);
                x += segment.Width;
                continue;
            }

            // The baseline lies `above` below the line's top, a run's own baseline its shift above that; the piece's top edge
            // is its ascent above its own baseline. An atomic inline's border box lies inside its margin box, whose bottom is
            // on the baseline. Text inside an inline box is moved to whole pixels of the page only once the page is known
            // (InlineBoxPixels).
            children.Add(item switch
            {
                AtomicInline { Fragment: { } fragment } atomic => new PlacedFragment(
                    new Point(x + atomic.Box.Style.Margin.Left.Points, above - atomic.Box.Style.Margin.Bottom.Points - fragment.Size.Height), fragment),
                ShapedRun run => new PlacedFragment(
                    new Point(x, above - extent!.Shift - extent.Ascent), new TextFragment(run.Run, run.Font, extent.Ascent, segment.Glyphs, new Size(segment.Width, extent.Ascent + extent.Descent))),
                _ => throw new InvalidOperationException("An atomic inline is placed on a line only once it is laid out."),
            });
            if (item is ShapedRun { Decorations.Count: > 0 } decorated)
            {
                // The piece's text was added last: its underlines and overlines go before it, its line-throughs after it.
                var ink = new TextDecorations.Ink(decorated.Font, decorated.Run.Style.FontSize.Points, decorated.Run.Style.DecorationSkipInk, outlines);
                var lines = TextDecorations.Of(decorated.Decorations, x, segment, above - extent!.Shift, extent.Ascent, rounding, ink);
                if (lines.IsFailure)
                {
                    return Result<LineFragment, Error>.Failure(lines.Error.WithSource(decorated.Run.Source));
                }

                children.InsertRange(children.Count - 1, lines.Value.Before);
                children.AddRange(lines.Value.After);
            }

            groups.Piece(item.Groups, firstChild, children.Count, x, x + segment.Width);
            x += segment.Width;
        }

        var parts = groups.End();

        // THE RULE FOR AN INLINE BOX OVER LINES, its limits, measured against Chromium's print: a justified line that a box goes
        // on from is widened by more than its room (each space 0.244 pt wider, the line 0.73 pt past its end, where the engine
        // widens nothing), and a line that ends at a shown soft hyphen inside a box is shaped again here without its hyphen.
        if (align == TextAlign.Justify && !isLastLine && parts.FirstOrDefault(part => part is { Group.Box: not null, HoldsEnd: false }) is { } justified)
        {
            return Result<LineFragment, Error>.Failure(LayoutErrors.InlineBox(justified.Group, "it breaks over two lines of a justified paragraph, which this version does not draw; keep its content on one line (white-space: nowrap) or align the paragraph to the start"));
        }

        if (pieces.FirstOrDefault(piece => piece.Run is ShapedRun && piece.Segment.Glyphs.Count > 0 && piece.Segment.Glyphs[^1].Text.EndsWith('\u00AD') && piece.Run.Groups.Any(group => group.Node.Box is not null)) is { Run: { } hyphenated })
        {
            return Result<LineFragment, Error>.Failure(LayoutErrors.InlineBox(hyphenated.Groups.Last(group => group.Node.Box is not null).Node, "it breaks over two lines at a soft hyphen, which this version does not draw; leave out the soft hyphen or keep the word on one line"));
        }

        return Result<LineFragment, Error>.Success(new LineFragment(new Size(x, above + below), children.ToImmutable()) { Baseline = above, Groups = parts });
    }

    /// <summary>
    /// The parts of the inline groups on one line (<see cref="LineGroupPart"/>), built in one pass over the line's pieces:
    /// a group's part opens at the first piece in it and closes before the first piece outside it, so the parts nest as
    /// the groups do. THE RULE FOR A LINK'S AREA (as Chromium 159's print writes a link: one rectangle per line part of
    /// it): in the inline direction from the start of the group's first piece on the line
    /// to the end of its last, so a space removed at the line's end is outside it and a space between two of its pieces
    /// inside it; in the block direction from the ascent of the group's own font above the group's own baseline to its
    /// descent below it, at the group's font size and under the profile's font metric rounding, whatever the sizes of the
    /// runs inside it (Chromium: a 14 pt run inside a link of 10.5 pt text does not make its rectangle taller). The group's
    /// own baseline is the line's, raised or lowered by the group's baseline shift: a link inside a <c>sup</c> or a
    /// <c>sub</c>, or itself raised, has its area around its shifted text, as Chromium writes its rectangle (cases
    /// <c>va-link</c>, <c>va-sub</c>, <c>va-decorated</c>), and so has the border box of an inline box on a raised element
    /// (<c>va-box</c>). The edges are exact, not rounded to whole pixels as Chromium writes them: they are not printed.
    /// <para>THE RULE FOR AN INLINE BOX OVER LINES (<c>box-decoration-break: slice</c>, CSS Fragmentation 3 §5.4): a group with a
    /// box has a part on each line it lies on, its border box from its first to its last piece there; the start edge (left
    /// margin, border and padding) lies in the first part only and the end edge in the last only, each where the line
    /// breaker put it (THE RULE FOR AN INLINE BOX'S EDGES IN A LINE), and the top and bottom padding and border on every
    /// part. As Chromium prints it: a box from "aa" to "ff" over two lines paints 54..98.25 pt and 18..73.5 pt, a box inside
    /// it its own parts over them (case <c>ib-break-nested</c>); a box whose first character is a space at the end of a line
    /// paints its start padding alone on that line, 82.5..87 pt after the word, and its text from the next line's start;
    /// one whose last character is a space there ends its background 3 pt (its padding) after the last glyph, the space
    /// carrying none (<c>ib-break-space</c>); centred and right-aligned lines place each part with the line
    /// (<c>ib-break-align</c>); a fractional top padding moves the text of every part by its own rounding
    /// (<c>ib-break-fraction</c>); between two lines over a page break, a part on each page (<c>ib-break-page</c>).</para>
    /// </summary>
    private sealed class LineGroups(double baseline, FontMetricRounding rounding)
    {
        /// <summary>The open parts, outermost first: the group, its first child, its left and right edges, where it stands in
        /// <see cref="parts"/>, and whether its start and end edge lie on this line.</summary>
        private readonly List<(Group Group, int First, double Left, double Right, int Part, bool Start, bool End)> open = [];
        private readonly List<LineGroupPart> parts = [];

        /// <summary>The end (exclusive) of the children of the last piece.</summary>
        private int lastEnd;

        /// <summary>A piece of the line: its groups, its children <paramref name="first"/> up to before <paramref name="end"/>,
        /// and its left and right edges.</summary>
        public void Piece(ValueList<Group> groups, int first, int end, double left, double right)
        {
            var common = 0;
            while (common < open.Count && common < groups.Count && open[common].Group.Node.Id == groups[common].Node.Id)
            {
                common++;
            }

            Close(common);
            for (var index = open.Count; index < groups.Count; index++)
            {
                // A part is added when it opens, so outer parts come before the parts inside them; its area and count are set
                // when it closes.
                open.Add((groups[index], first, left, right, parts.Count, false, false));
                parts.Add(new LineGroupPart(groups[index].Node, default, first, 0));
            }

            for (var index = 0; index < open.Count; index++)
            {
                open[index] = open[index] with { Right = right };
            }

            lastEnd = end;
        }

        /// <summary>The edge of an inline box, just passed to <see cref="Piece"/>: its group is the innermost open part.</summary>
        public void Edge(Edge edge) =>
            open[^1] = edge.Side == InlineBoxEdge.Start ? open[^1] with { Start = true } : open[^1] with { End = true };

        /// <summary>The group's own baseline below the line's top: the line's baseline, raised or lowered by the group's
        /// <see cref="TextStyle.BaselineShift"/> (THE RULE FOR A SHIFTED RUN ON THE LINE). A group on a raised or lowered
        /// element, or inside one, has the element's shift in its style, as its text has.</summary>
        private double BaselineOf(Group group) => baseline - group.Node.Style.BaselineShift.Points;

        /// <summary>The top of a group's border box below the line's top: its font's ascent above its own baseline, then its
        /// top padding and border.</summary>
        private double BorderTop(Group group)
        {
            var (style, box) = (group.Node.Style, group.Node.Box!);
            var extent = InlineExtent.Of(group.Font, style.FontSize.Points, style.LineHeight, rounding);
            return BaselineOf(group) - extent.Ascent - box.Padding.Top.Points - box.Border.Top.Width.Points;
        }

        /// <summary>Closes every part still open after the last piece.</summary>
        public ValueList<LineGroupPart> End()
        {
            Close(0);
            return [.. parts];
        }

        private void Close(int keep)
        {
            while (open.Count > keep)
            {
                var (group, first, left, right, part, start, end) = open[^1];
                open.RemoveAt(open.Count - 1);
                var style = group.Node.Style;
                var extent = InlineExtent.Of(group.Font, style.FontSize.Points, style.LineHeight, rounding);
                parts[part] = new LineGroupPart(group.Node, new Rect(new Point(left, BaselineOf(group) - extent.Ascent), new Size(right - left, extent.Ascent + extent.Descent)), first, lastEnd - first)
                {
                    Box = group.Node.Box is { } box ? BorderBox(group, box, left, right, extent) : null,
                    HoldsStart = start,
                    HoldsEnd = end,
                };
            }
        }

        /// <summary>
        /// The border box of a group's box on the line (THE RULE FOR AN INLINE BOX OVER LINES): in the inline direction from
        /// after its start margin, or from its first piece on the line where its start edge lies on an earlier line, to before
        /// its end margin, or to the end of its last piece where its end edge lies on a later line, in the block direction from its top border and padding above its font's ascent to its bottom
        /// padding and border below its descent (CSS 2.1 §10.6.1: the height of an inline box's content area is that of its
        /// font, and its vertical padding, border and margin start from it), exact: under
        /// <see cref="InlineBoxSnapping.WholePixels"/> its edges are put on whole pixels of the page once the page is known
        /// (<see cref="InlineBoxPixels"/>).
        /// </summary>
        private Rect BorderBox(Group group, InlineBoxStyle box, double left, double right, InlineExtent extent)
        {
            var top = BorderTop(group);
            var bottom = BaselineOf(group) + extent.Descent + box.Padding.Bottom.Points + box.Border.Bottom.Width.Points;
            return new Rect(new Point(left, top), new Size(right - left, bottom - top));
        }
    }

    /// <summary>
    /// The runs that put no glyph on any line (an empty span, a span whose only space collapses at a line edge), each with
    /// the line it lies on: the line of the last run before it that has glyphs, or the first line. "Empty inline elements
    /// generate empty inline boxes, but these boxes still have margins, padding, borders and a line height, and thus
    /// influence the line box height calculation just like elements with content" (CSS 2.1 §10.8), as Chromium counts
    /// them (case <c>fr-empty-span</c>).
    /// </summary>
    /// <param name="lines">The paragraph's lines.</param>
    /// <param name="runs">The paragraph's runs.</param>
    /// <returns>Per line, the runs without glyphs that lie on it.</returns>
    public static IReadOnlyList<IReadOnlyList<ShapedRun>> EmptyRunsByLine(IReadOnlyList<TextLine> lines, IReadOnlyList<Item> runs)
    {
        // One pass over the segments and one over the runs: linear in the paragraph.
        var lastLineOf = new int?[runs.Count];
        for (var line = 0; line < lines.Count; line++)
        {
            foreach (var segment in lines[line].Segments)
            {
                lastLineOf[segment.Run] = line;
            }
        }

        var byLine = Enumerable.Range(0, lines.Count).Select(_ => new List<ShapedRun>()).ToList();
        var current = 0;
        for (var run = 0; run < runs.Count && lines.Count > 0; run++)
        {
            if (lastLineOf[run] is { } line)
            {
                // A run on several lines: an empty run after it lies on its last line.
                current = line;
                continue;
            }

            // An atomic inline always has its glyph on a line.
            if (runs[run] is ShapedRun empty)
            {
                byLine[current].Add(empty);
            }
        }

        return byLine;
    }
}

/// <summary>
/// The CSS pixel, 1/96 inch = 0.75 pt (CSS Values 4 §6.2), and rounding lengths in points to whole pixels, as Chromium
/// does where it keeps whole pixels (<see cref="FontMetricRounding.WholePixels"/>).
/// </summary>
internal static class CssPixels
{
    /// <summary>Points per CSS pixel.</summary>
    public const double Points = 0.75;

    /// <summary>A length rounded to the nearest whole pixel, half away from zero (C's <c>lroundf</c>, which Chromium
    /// uses).</summary>
    /// <param name="points">The length in points.</param>
    /// <returns>The rounded length in points.</returns>
    public static double Round(double points) => Math.Round(points / Points, MidpointRounding.AwayFromZero) * Points;

    /// <summary>A length floored to a whole pixel (<c>LayoutUnit::Floor</c>). A length a hair below a whole pixel only
    /// because points do not hold every pixel value exactly counts as that pixel.</summary>
    /// <param name="points">The length in points.</param>
    /// <returns>The floored length in points.</returns>
    public static double Floor(double points) => Math.Floor(points / Points + 1e-9) * Points;
}

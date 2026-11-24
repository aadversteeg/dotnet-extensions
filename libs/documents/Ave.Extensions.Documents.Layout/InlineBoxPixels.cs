using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// Puts the inline boxes of a page on whole pixels of the page (<see cref="InlineBoxSnapping.WholePixels"/>), and the
/// bullets of its list items (<see cref="MarkerSnapping.WholePixels"/>), and cuts the gaps of its underlines and overlines
/// around the ink again with the band at the line's unrounded place (<see cref="InkGapBand.AtUnroundedLine"/>), once the
/// page is laid out: layout builds a line before it knows where the line lies on its page (a paragraph in a table cell is
/// placed only after the cell is laid out), so it keeps each box and each bullet exact (<see cref="LineGroupPart.Box"/>,
/// <see cref="ListItemLayout"/>) and cuts the gaps as if the line's top were on a whole pixel, and this pass, which walks
/// the page with every fragment's place on it, rounds them and cuts again where the line's top is not.
/// </summary>
/// <remarks>
/// THE RULE FOR AN INLINE BOX ON WHOLE PIXELS: each edge of the box's border box lies on the nearest whole pixel of the page,
/// and the text inside the innermost box is drawn from that box's top rounded so, where the text outside the box is drawn
/// from the line's top rounded so. The engine draws the text of a line at its exact place (a reader rounds the line's top as
/// Chromium does), so the text inside the box is moved by the difference of the two roundings: a box whose top lies 0.53 px
/// above the line's top moves its text up by 0.47 px where the line's top is on a whole pixel and down by 0.53 px where
/// it lies a third of a pixel below one. Measured against Chromium's print (<c>tools/reference/chromium</c>): a page margin
/// of 10 pt puts the page area at 13 px, so the page's pixels and the page area's are the same; with a body margin, a
/// paragraph margin and padding in points the line starts at 26.3125 px, and a box from 120.75 px is painted from 121 px
/// (in the line's own pixels: 120.3125; case <c>ib-origin-margin</c>); the same in a table cell with padding in points
/// (<c>ib-origin-cell</c>); on the second page, whose page area starts at 13 px again, a line whose top lies at 37.56 px
/// draws the text of a box 1.453 px taller than its font from the box's top at 36 px, not at 37 (<c>ib-origin-page</c>).
/// </remarks>
internal static class InlineBoxPixels
{
    /// <summary>One fragment on the way down: its place on the page, the children it holds, how many of them are done, and
    /// the children rebuilt so far.</summary>
    private sealed class Frame(PlacedFragment placed, Point origin, IReadOnlyList<PlacedFragment> children)
    {
        public PlacedFragment Placed { get; } = placed;

        public Point Origin { get; } = origin;

        public IReadOnlyList<PlacedFragment> Children { get; } = children;

        public List<PlacedFragment> Done { get; } = [];

        public bool Changed { get; set; }
    }

    /// <summary>The policies the pass follows: which of its three jobs it does.</summary>
    /// <param name="Boxes">Inline boxes go on whole pixels (<see cref="InlineBoxSnapping.WholePixels"/>).</param>
    /// <param name="Bullets">Bullets go on whole pixels (<see cref="MarkerSnapping.WholePixels"/>).</param>
    /// <param name="Gaps">Gaps around the ink are cut at the line's unrounded place (<see cref="InkGapBand.AtUnroundedLine"/>).</param>
    public readonly record struct Policies(bool Boxes, bool Bullets, bool Gaps)
    {
        /// <summary>The policies of a profile.</summary>
        /// <param name="profile">The document's profile.</param>
        /// <returns>The policies.</returns>
        public static Policies Of(LayoutProfile profile) => new(
            profile.InlineBoxSnapping == InlineBoxSnapping.WholePixels,
            profile.MarkerSnapping == MarkerSnapping.WholePixels,
            profile.InkGapBand == InkGapBand.AtUnroundedLine);

        /// <summary>True when the pass has nothing to do.</summary>
        public bool None => !Boxes && !Bullets && !Gaps;
    }

    /// <summary>A fragment of a page with every inline box in it on whole pixels of the page, every bullet, and the gaps of
    /// every underline and overline cut at its line's place on the page, each as the policies ask.</summary>
    /// <param name="placed">The fragment, its offset relative to the page's top-left corner.</param>
    /// <param name="policies">What the pass does.</param>
    /// <returns>The fragment with its lines and its bullets rebuilt where they change, the same fragment where nothing
    /// does; or the error of a glyph outline that cannot be decoded.</returns>
    public static Result<PlacedFragment, Error> Snap(PlacedFragment placed, Policies policies)
    {
        if (placed.Fragment is LineFragment top)
        {
            return Line(top, placed.Offset, policies).OnSuccessMap(line => ReferenceEquals(line, top) ? placed : placed with { Fragment = line });
        }

        // Without recursion: fragments nest as deep as the document's boxes.
        var pending = new Stack<Frame>();
        pending.Push(new Frame(placed, placed.Offset, ChildrenOf(placed.Fragment)));
        while (true)
        {
            var frame = pending.Peek();
            if (frame.Done.Count < frame.Children.Count)
            {
                var child = frame.Children[frame.Done.Count];
                var at = frame.Origin + child.Offset;
                if (child.Fragment is LineFragment line)
                {
                    var rebuilt = Line(line, at, policies);
                    if (rebuilt.IsFailure)
                    {
                        return Result<PlacedFragment, Error>.Failure(rebuilt.Error);
                    }

                    var snapped = rebuilt.Value;
                    frame.Changed |= !ReferenceEquals(snapped, line);
                    frame.Done.Add(ReferenceEquals(snapped, line) ? child : child with { Fragment = snapped });
                }
                else if (policies.Bullets && child.Fragment is BoxFragment { Box.Role: SemanticRole.ListLabelRole, Children: [{ Fragment: MarkerFragment }] })
                {
                    // THE RULE FOR A BULLET ON WHOLE PIXELS (MarkerSnapping.WholePixels): its box's left and top edges on
                    // the nearest whole pixel of the page; its size is whole pixels already (ListItemLayout).
                    var shift = new Point(CssPixels.Round(at.X) - at.X, CssPixels.Round(at.Y) - at.Y);
                    frame.Changed |= shift != new Point(0, 0);
                    frame.Done.Add(shift == new Point(0, 0) ? child : child with { Offset = child.Offset + shift });
                }
                else if (ChildrenOf(child.Fragment) is { Count: > 0 } children)
                {
                    pending.Push(new Frame(child, at, children));
                }
                else
                {
                    frame.Done.Add(child);
                }

                continue;
            }

            pending.Pop();
            var done = !frame.Changed ? frame.Placed : frame.Placed with
            {
                Fragment = frame.Placed.Fragment switch
                {
                    BoxFragment box => box with { Children = [.. frame.Done] },
                    SliceFragment slice => slice with { Content = frame.Done[0] },
                    var other => other,
                },
            };
            if (pending.Count == 0)
            {
                return Result<PlacedFragment, Error>.Success(done);
            }

            pending.Peek().Done.Add(done);
            pending.Peek().Changed |= frame.Changed;
        }
    }

    /// <summary>
    /// The first inline group whose box reaches above <paramref name="top"/> on a page, or null. THE RULE FOR AN INLINE BOX
    /// OVER A PAGE BREAK: a box broken between two of its lines is two parts on two pages, each painted on its own page (case
    /// <c>ib-break-page</c>); a box on a line that starts a later page and whose top lies above that page's area is refused,
    /// since Chromium paints it there and also paints the part above the page area at the bottom of the page before, in
    /// its place in the continuous flow (measured: a box 31 px high whose line moved to page 2 shows a strip at page 1's end).
    /// </summary>
    /// <param name="placed">A page's body, its offset relative to the page's top-left corner.</param>
    /// <param name="top">The top of the page area, in points from the page's top.</param>
    /// <returns>The group, or null.</returns>
    public static InlineGroup? AboveArea(PlacedFragment placed, double top)
    {
        var pending = new Stack<(Fragment Fragment, Point Origin)>();
        pending.Push((placed.Fragment, placed.Offset));
        while (pending.Count > 0)
        {
            var (fragment, origin) = pending.Pop();
            if (fragment is LineFragment line)
            {
                if (line.Groups.FirstOrDefault(part => part.Box is { } box && origin.Y + box.Origin.Y < top - 1e-9) is { } above)
                {
                    return above.Group;
                }

                continue;
            }

            foreach (var child in ChildrenOf(fragment))
            {
                pending.Push((child.Fragment, origin + child.Offset));
            }
        }

        return null;
    }

    private static IReadOnlyList<PlacedFragment> ChildrenOf(Fragment fragment) => fragment switch
    {
        BoxFragment box => box.Children,
        SliceFragment slice => [slice.Content],
        _ => [],
    };

    /// <summary>A line at <paramref name="origin"/> on the page with its boxes on whole pixels of the page and the text
    /// inside each moved with it (THE RULE FOR AN INLINE BOX ON WHOLE PIXELS), and the gaps of its underlines and overlines
    /// cut again at the line's place on the page (<see cref="InkGapBand.AtUnroundedLine"/>), as the policies ask; the same
    /// line when neither changes it.</summary>
    private static Result<LineFragment, Error> Line(LineFragment line, Point origin, Policies policies)
    {
        var boxes = policies.Boxes && line.Groups.Any(part => part.Box is not null);
        var rounding = CssPixels.Round(origin.Y) - origin.Y;
        var gaps = policies.Gaps && line.InkCuts.Count > 0;
        if (!boxes && !gaps)
        {
            return Result<LineFragment, Error>.Success(line);
        }

        // How far the text of each child is moved down from where the line was built: by the rounding of its box's top.
        var shifts = new double[line.Children.Count];
        if (boxes)
        {
            for (var index = 0; index < shifts.Length; index++)
            {
                if (line.Children[index].Fragment is TextFragment && Innermost(line.Groups, index) is { } box)
                {
                    var top = box.Origin.Y;
                    shifts[index] = CssPixels.Round(origin.Y + top) - CssPixels.Round(origin.Y) - top;
                }
            }
        }

        var children = line.Children.Select((child, index) => shifts[index] == 0 ? child : child with { Offset = child.Offset + new Point(0, shifts[index]) }).ToList();
        var groups = boxes ? [.. line.Groups.Select(part => part.Box is { } box ? part with { Box = OnPixels(box, origin) } : part)] : line.Groups;
        if (!gaps)
        {
            return Result<LineFragment, Error>.Success(line with { Children = [.. children], Groups = groups });
        }

        return Recut(children, groups, line.InkCuts, shifts, rounding).OnSuccessMap(recut => line with { Children = recut.Children, Groups = recut.Groups });
    }

    /// <summary>
    /// THE RULE FOR THE GAPS ON THE PAGE (<see cref="InkGapBand.AtUnroundedLine"/>): the text of a line is drawn from the
    /// line's top rounded to the nearest whole pixel of the page (a reader of the engine's print rounds it so; the text of an
    /// inline box from its box's rounded top, <see cref="Line"/>) and its underlines and overlines painted from there, but the
    /// gaps are cut with the band at the line's unrounded place. So each decorated piece's lines are cut again with the band
    /// moved up by the line top's rounding and by the shift of the piece's text; its rectangles take the place of the ones cut
    /// when the line was built, and the parts of the inline groups are counted again over the new children. A line whose
    /// top lies on a whole pixel, and whose text is not moved, keeps its rectangles.
    /// </summary>
    private static Result<(ValueList<PlacedFragment> Children, ValueList<LineGroupPart> Groups), Error> Recut(
        List<PlacedFragment> children, ValueList<LineGroupPart> groups, ValueList<TextDecorations.Cut> cuts, double[] shifts, double rounding)
    {
        // The new index of every old child, and of the end: a piece's old lines all map to where its new ones start.
        var starts = new int[children.Count + 1];
        var rebuilt = new List<PlacedFragment>(children.Count);
        var next = 0;
        for (var index = 0; index < children.Count;)
        {
            if (next < cuts.Count && cuts[next].Text - cuts[next].Before == index)
            {
                var cut = cuts[next++];
                var band = -(rounding + shifts[cut.Text]);
                if (System.Math.Abs(band) > 1e-9)
                {
                    var again = cut.Again(band);
                    if (again.IsFailure)
                    {
                        return Result<(ValueList<PlacedFragment>, ValueList<LineGroupPart>), Error>.Failure(again.Error);
                    }

                    for (; index < cut.Text; index++)
                    {
                        starts[index] = rebuilt.Count;
                    }

                    rebuilt.AddRange(again.Value);
                    continue;
                }
            }

            starts[index] = rebuilt.Count;
            rebuilt.Add(children[index]);
            index++;
        }

        starts[children.Count] = rebuilt.Count;
        return Result<(ValueList<PlacedFragment>, ValueList<LineGroupPart>), Error>.Success(
            ([.. rebuilt], [.. groups.Select(part => part with { First = starts[part.First], Count = starts[part.First + part.Count] - starts[part.First] })]));
    }

    /// <summary>The exact border box of the innermost part with a box that holds the line's child <paramref name="index"/>:
    /// parts come outer first, so the last that holds it.</summary>
    private static Rect? Innermost(IReadOnlyList<LineGroupPart> parts, int index)
    {
        Rect? found = null;
        foreach (var part in parts)
        {
            if (part.Box is { } box && part.First <= index && index < part.First + part.Count)
            {
                found = box;
            }
        }

        return found;
    }

    /// <summary>A rectangle relative to a line at <paramref name="origin"/>, each edge moved to the nearest whole pixel of
    /// the page.</summary>
    private static Rect OnPixels(Rect box, Point origin)
    {
        var (left, top) = (CssPixels.Round(origin.X + box.Origin.X) - origin.X, CssPixels.Round(origin.Y + box.Origin.Y) - origin.Y);
        var (right, bottom) = (CssPixels.Round(origin.X + box.Origin.X + box.Size.Width) - origin.X, CssPixels.Round(origin.Y + box.Origin.Y + box.Size.Height) - origin.Y);
        return new Rect(new Point(left, top), new Size(right - left, bottom - top));
    }
}

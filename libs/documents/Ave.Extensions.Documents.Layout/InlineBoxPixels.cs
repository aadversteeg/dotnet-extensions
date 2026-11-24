using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// Puts the inline boxes of a page on whole pixels of the page (<see cref="InlineBoxSnapping.WholePixels"/>), once the page
/// is laid out: layout builds a line before it knows where the line lies on its page (a paragraph in a table cell is placed
/// only after the cell is laid out), so it keeps each box exact (<see cref="LineGroupPart.Box"/>) and this pass, which walks
/// the page with every fragment's place on it, rounds them.
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

    /// <summary>A fragment of a page with every inline box in it on whole pixels of the page.</summary>
    /// <param name="placed">The fragment, its offset relative to the page's top-left corner.</param>
    /// <returns>The fragment with its lines that hold a box rebuilt; the same fragment when it holds none.</returns>
    public static PlacedFragment Snap(PlacedFragment placed)
    {
        if (placed.Fragment is LineFragment top)
        {
            return placed with { Fragment = Line(top, placed.Offset) };
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
                    var snapped = Line(line, at);
                    frame.Changed |= !ReferenceEquals(snapped, line);
                    frame.Done.Add(ReferenceEquals(snapped, line) ? child : child with { Fragment = snapped });
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
            var rebuilt = !frame.Changed ? frame.Placed : frame.Placed with
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
                return rebuilt;
            }

            pending.Peek().Done.Add(rebuilt);
            pending.Peek().Changed |= frame.Changed;
        }
    }

    private static IReadOnlyList<PlacedFragment> ChildrenOf(Fragment fragment) => fragment switch
    {
        BoxFragment box => box.Children,
        SliceFragment slice => [slice.Content],
        _ => [],
    };

    /// <summary>A line at <paramref name="origin"/> on the page with its boxes on whole pixels of the page and the text
    /// inside each moved with it (THE RULE FOR AN INLINE BOX ON WHOLE PIXELS); the same line when it holds no box.</summary>
    private static LineFragment Line(LineFragment line, Point origin)
    {
        if (!line.Groups.Any(part => part.Box is not null))
        {
            return line;
        }

        var children = line.Children.ToArray();
        for (var index = 0; index < children.Length; index++)
        {
            if (children[index].Fragment is TextFragment && Innermost(line.Groups, index) is { } box)
            {
                var top = box.Origin.Y;
                var shift = CssPixels.Round(origin.Y + top) - CssPixels.Round(origin.Y) - top;
                children[index] = children[index] with { Offset = children[index].Offset + new Point(0, shift) };
            }
        }

        return line with
        {
            Children = [.. children],
            Groups = [.. line.Groups.Select(part => part.Box is { } box ? part with { Box = OnPixels(box, origin) } : part)],
        };
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

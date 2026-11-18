using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.Typesetting;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// The tests' own check of the rule for a row that a page break cuts (fix round 5, SP-1; M2 A3), read from the placed
/// result only: a part of a body row that the row has on another page as well — at the end of a page, where the row goes
/// on, or at the start of one, where it came from — must have something drawn in it by a cell, where it shows: a
/// visible glyph, an image, a rule, or a filled or bordered box, not wholly under a grid line of the table. A cell's own
/// background is not counted: it is the row's decoration and covers every part of the row, whatever is in it.
/// Written apart from the layout's code on purpose, so that it checks that code instead of repeating it. Also linked
/// into the Fluent tests.
/// </summary>
internal static class RowParts
{
    private const double Epsilon = 1e-6;

    /// <summary>A body row part on a page that the row has on another page too and that shows nothing.</summary>
    /// <param name="Page">The page, from 1.</param>
    /// <param name="Row">The row's box id.</param>
    /// <param name="Top">The part's top, from the table part's top.</param>
    /// <param name="Height">The part's height.</param>
    public sealed record EmptyStrip(int Page, NodeId Row, double Top, double Height)
    {
        public override string ToString() => $"page {Page}: row {Row} at {Top:0.###}, {Height:0.###} high, is cut and shows nothing";
    }

    /// <summary>Every row part that shows nothing and whose row is on another page too, in every table on the pages
    /// (top-level tables; a table inside a cell is that cell's content).</summary>
    public static IReadOnlyList<EmptyStrip> EmptyStrips(IReadOnlyList<PageFragment> pages)
    {
        var parts = pages.Select((page, index) => Tables(page.Root.Fragment).SelectMany(BodyRows).Select(row => (Page: index + 1, row.Table, row.Row, row.Top)).ToList()).ToList();
        var strips = new List<EmptyStrip>();
        for (var page = 0; page < parts.Count; page++)
        {
            var elsewhere = parts.Where((_, other) => other != page).SelectMany(rows => rows).Select(row => row.Row.Box.Id).ToHashSet();
            foreach (var (number, table, row, top) in parts[page].Where(part => part.Row.Size.Height > Epsilon && elsewhere.Contains(part.Row.Box.Id)))
            {
                var bottom = top + row.Size.Height;
                var rules = HorizontalRules(table).ToList();
                var shown = Cells(table).SelectMany(cell => cell.Fragment.Children
                    .SelectMany(child => Drawn(child.Fragment, cell.Top + child.Offset.Y))
                    .Where(area => !rules.Any(rule => rule.Left < cell.Left + cell.Fragment.Size.Width - Epsilon && rule.Right > cell.Left + Epsilon
                        && rule.Top <= Math.Max(area.Top, top) + Epsilon && rule.Bottom >= Math.Min(area.Bottom, bottom) - Epsilon)));
                if (!shown.Any(area => area.Bottom > top + Epsilon && area.Top < bottom - Epsilon))
                {
                    strips.Add(new EmptyStrip(number, row.Box.Id, top, row.Size.Height));
                }
            }
        }

        return strips;
    }

    /// <summary>The horizontal grid lines a table part draws, with their extent in the part.</summary>
    private static IEnumerable<(double Left, double Right, double Top, double Bottom)> HorizontalRules(BoxFragment table) =>
        table.Children
            .Where(child => child.Fragment is RuleFragment rule && rule.Size.Width > rule.Size.Height)
            .Select(child => (child.Offset.X, child.Offset.X + child.Fragment.Size.Width, child.Offset.Y, child.Offset.Y + child.Fragment.Size.Height));

    /// <summary>The top-level tables of a fragment tree.</summary>
    private static IEnumerable<BoxFragment> Tables(Fragment fragment) =>
        fragment is not BoxFragment box ? []
        : box.Box.Role == SemanticRole.Table ? [box]
        : box.Children.SelectMany(child => Tables(child.Fragment));

    /// <summary>The body rows of a table part (not its header rows) with their tops in the part.</summary>
    private static IEnumerable<(BoxFragment Table, BoxFragment Row, double Top)> BodyRows(BoxFragment table) =>
        table.Children
            .Where(group => group.Fragment is BoxFragment { IsRepeated: false } box && box.Box.Role != SemanticRole.TableHead)
            .SelectMany(group => ((BoxFragment)group.Fragment).Children.Select(row => (table, (BoxFragment)row.Fragment, group.Offset.Y + row.Offset.Y)));

    /// <summary>Every cell of a table part with its left and top in the part (a cell that spans rows sits in its first row).</summary>
    private static IEnumerable<(BoxFragment Fragment, double Left, double Top)> Cells(BoxFragment table) =>
        table.Children
            .Where(group => group.Fragment is BoxFragment)
            .SelectMany(group => ((BoxFragment)group.Fragment).Children.SelectMany(row =>
                ((BoxFragment)row.Fragment).Children.Select(cell => ((BoxFragment)cell.Fragment, group.Offset.X + row.Offset.X + cell.Offset.X, group.Offset.Y + row.Offset.Y + cell.Offset.Y))));

    /// <summary>The vertical extent of everything the fragment draws, at <paramref name="top"/>; empty areas left out.</summary>
    private static IEnumerable<(double Top, double Bottom)> Drawn(Fragment fragment, double top)
    {
        IEnumerable<(double Top, double Bottom)> areas = fragment switch
        {
            BoxFragment box => Decoration(box, top).Concat(box.Children.SelectMany(child => Drawn(child.Fragment, top + child.Offset.Y))),
            LineFragment line => line.Children.SelectMany(child => Drawn(child.Fragment, top + child.Offset.Y)),
            TextFragment text when text.Glyphs.Any(Shows) => [(top, top + text.Size.Height)],
            ImageFragment or RuleFragment when fragment.Size.Width > Epsilon => [(top, top + fragment.Size.Height)],
            // A slice of content taller than a page draws its content clipped to its own band.
            SliceFragment slice => Drawn(slice.Content.Fragment, top + slice.Content.Offset.Y)
                .Select(area => (Math.Max(area.Top, top), Math.Min(area.Bottom, top + slice.Size.Height))),
            _ => [],
        };
        return areas.Where(area => area.Bottom - area.Top > Epsilon);
    }

    /// <summary>A box's background and the visible sides of the border it paints; nothing for a box of no width.</summary>
    private static IEnumerable<(double Top, double Bottom)> Decoration(BoxFragment box, double top)
    {
        if (box.Size.Width <= Epsilon)
        {
            yield break;
        }

        var (border, height) = (box.Border, box.Size.Height);
        var (upper, lower) = (border.Top.Width.Points, border.Bottom.Width.Points);
        if (box.Box.Style.Background is not null)
        {
            yield return (top, top + height);
        }

        if (border.Top.IsVisible)
        {
            yield return (top, top + upper);
        }

        if (border.Bottom.IsVisible)
        {
            yield return (top + height - lower, top + height);
        }

        if (border.Left.IsVisible || border.Right.IsVisible)
        {
            yield return (top + upper, top + height - lower);
        }
    }

    /// <summary>A glyph that leaves ink: not a default-ignorable character or a line break, and not white space.</summary>
    private static bool Shows(ShapedGlyph glyph) =>
        !glyph.IsInvisible && !glyph.IsLineBreak && !(glyph.Text.Length > 0 && glyph.Text.All(char.IsWhiteSpace));
}

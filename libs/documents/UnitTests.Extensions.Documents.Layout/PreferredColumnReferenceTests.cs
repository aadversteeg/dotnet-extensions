using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// A column with a given width in the automatic table layout (<see cref="TrackSize.Preferred"/>) against Chromium's
/// layout of <c>col</c> lengths (case <c>tw-pref</c> in <c>tools/reference/chromium/cases</c>): collapsed borders, Lato
/// 10.5pt (14px), reference cells (<c>border: 0.75pt solid; padding: 2pt 4pt</c>), each table with <c>width: auto</c>
/// (<see cref="BoxWidth.Auto"/>, names ending in <c>-a</c>) and <c>width: 100%</c> (<see cref="BoxWidth.Stretch"/>,
/// <c>-f</c>).
/// </summary>
public class PreferredColumnReferenceTests
{
    private const double ExactSize = 10.5;

    private static ValueList<PageFragment> Paginate(StyledDocument document)
    {
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    private static IEnumerable<BoxFragment> Boxes(Fragment fragment, SemanticRole role) =>
        fragment is BoxFragment box
            ? (box.Box.Role == role ? [box] : Enumerable.Empty<BoxFragment>()).Concat(box.Children.SelectMany(child => Boxes(child.Fragment, role)))
            : [];

    private static TrackSize Pref(double points) => new TrackSize.Preferred(points.Pt());

    private static TrackSize Auto => TrackSize.Auto.Instance;

    /// <summary>The document of one measured table, written in code as Chromium has it in its case file.</summary>
    private static StyledDocument Document(string name)
    {
        var t = new TableModels(ExactSize);
        var width = name.EndsWith("-a", StringComparison.Ordinal) ? BoxWidth.Auto : BoxWidth.Stretch;
        StyledDocument Page(double container, ValueList<TrackSize> columns, params Box[] rows)
        {
            var table = t.Table(columns, t.Body(rows));
            return TableModels.Page(container, 400, table with { Style = table.Style with { Width = width } });
        }

        var span = TableModels.ReferenceCell with { Span = new CellSpan(2, 1) };
        return name[..name.LastIndexOf('-')] switch
        {
            "narrow" => Page(300, [Pref(90), Auto], t.Row(t.Cell("short"), t.Cell("a longer text"))),
            "wide" => Page(300, [Pref(30), Auto], t.Row(t.Cell("one two three four"), t.Cell("short"))),
            "squeeze" => Page(150, [Pref(120), Pref(60)], t.Row(t.Cell("short"), t.Cell("one two three"))),
            "squeeze-auto" => Page(200, [Pref(120), Auto], t.Row(t.Cell("short"), t.Cell("alpha beta gamma delta epsilon"))),
            "squeeze-auto-min" => Page(100, [Pref(120), Auto], t.Row(t.Cell("short words"), t.Cell("alpha beta gamma"))),
            "all" => Page(300, [Pref(60), Pref(30), Pref(90)], t.Row(t.Cell("short"), t.Cell("Supercalifragilistic"), t.Cell("a longer text"))),
            "beside" => Page(300, [Pref(30), Auto, Auto], t.Row(t.Cell("Supercalifragilistic"), t.Cell("one"), t.Cell("two words"))),
            "span" => Page(300, [Pref(60), Auto, Auto],
                t.Row(t.Cell("a much wider heading over both columns", span), t.Cell("third")), t.Row(t.Cell("one"), t.Cell("two"), t.Cell("x"))),
            "span-min" => Page(150, [Pref(60), Auto], t.Row(t.Cell("Supercalifragilisticexpialidocious", span)), t.Row(t.Cell("a"), t.Cell("b"))),
            "span-pref" => Page(300, [Pref(60), Pref(30)], t.Row(t.Cell("Supercalifragilisticexpialidocious and more", span)), t.Row(t.Cell("a"), t.Cell("b"))),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "no such case"),
        };
    }

    /// <summary>
    /// One measured table, cell by cell, and the table's width. What Chromium shows: a column keeps its width when the
    /// content is narrower (<c>narrow</c>); a word wider than the width widens it to that word, not to the cell's
    /// max-content (<c>wide</c>, <c>beside</c>); a table too narrow for the widths shrinks them towards their
    /// min-content in proportion to (width − min-content), after the auto columns are at their minimum
    /// (<c>squeeze</c>, <c>squeeze-auto-min</c>), and does not overflow; with room, the auto columns take the rest and the
    /// preferred columns keep their width (<c>squeeze-auto</c>, <c>narrow-f</c>, <c>span-f</c>); without auto columns a
    /// 100% table shares the rest over the preferred columns in proportion to their maximums (<c>all-f</c>); a cell
    /// spanning a preferred and an auto column widens the auto column (<c>span</c>), one spanning only preferred columns
    /// widens them in proportion to their maximums for its minimum and not for its maximum (<c>span-pref</c>).
    /// The tolerance n counts each snapped input once: one ceiled text item per cell with text (the paddings are
    /// truncated alike in every column and a 0.75pt border is a whole pixel), the column's own truncated share, and one
    /// for a remainder or a container width Chromium truncates (200pt and 100pt are not whole pixels). It does not count
    /// that a proportion scales an input's error by the share's factor (target over the sum of the weights, up to about
    /// 3 here): that bound is not derived; the measured differences stay at or below 1.23 units.
    /// </summary>
    [Theory]
    [InlineData("narrow-a", new[] { 90.000, 66.363 }, 157.113)]
    [InlineData("narrow-f", new[] { 90.000, 209.250 }, 300)]
    [InlineData("wide-a", new[] { 33.059, 32.695 }, 66.504)]
    [InlineData("wide-f", new[] { 33.059, 266.191 }, 300)]
    [InlineData("squeeze-a", new[] { 96.492, 52.758 }, 150)]
    [InlineData("squeeze-f", new[] { 96.492, 52.758 }, 150)]
    [InlineData("squeeze-auto-a", new[] { 120.000, 79.242 }, 199.992)]
    [InlineData("squeeze-auto-f", new[] { 120.000, 79.242 }, 199.992)]
    [InlineData("squeeze-auto-min-f", new[] { 57.340, 41.906 }, 99.996)]
    [InlineData("all-a", new[] { 60.000, 92.098, 90.000 }, 242.848)]
    [InlineData("all-f", new[] { 74.156, 113.836, 111.258 }, 300)]
    [InlineData("beside-a", new[] { 92.098, 26.109, 57.281 }, 176.238)]
    [InlineData("beside-f", new[] { 92.098, 64.852, 142.301 }, 300)]
    [InlineData("span-a", new[] { 197.203, 30.387, 60.000, 137.203, 30.387 }, 228.340)]
    [InlineData("span-f", new[] { 255.867, 43.383, 60.000, 195.867, 43.383 }, 300)]
    [InlineData("span-pref-a", new[] { 157.055, 104.695, 52.359 }, 157.805)]
    public void A_column_with_a_given_width_in_the_automatic_layout_is_sized_like_Chromium(string name, double[] chromium, double table)
    {
        var page = Paginate(Document(name)).Single();

        var cells = Boxes(page.Root.Fragment, SemanticRole.TableCell).Select(cell => cell.Size.Width).ToArray();
        cells.Should().HaveSameCount(chromium);
        var n = chromium.Length + 2;
        for (var index = 0; index < cells.Length; index++)
        {
            ChromiumLayout.DifferenceInLayoutUnits(cells[index], chromium[index]).Should().BeLessThanOrEqualTo(
                n + ChromiumLayout.FloatSlack,
                $"{name} cell {index}: ours {cells[index]} must be within {n} layout units of Chromium's {chromium[index]} ({chromium.Length} ceiled text items, its own share, a remainder or the container)");
        }

        var ours = Boxes(page.Root.Fragment, SemanticRole.Table).Single().Size.Width;
        ChromiumLayout.DifferenceInLayoutUnits(ours, table).Should().BeLessThanOrEqualTo(n + ChromiumLayout.FloatSlack, $"{name} table: ours {ours}, Chromium's {table}");
    }

    [Fact]
    public void A_word_wider_than_the_table_still_fails_with_the_width_needed()
    {
        // span-min: Chromium lets the table overflow its 150pt container (157.805); the engine refuses, as for any
        // content wider than the page area (ColumnsDoNotFit).
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), Document("span-min-a"));

        result.IsFailure.Should().BeTrue();
        result.Error.ToString().Should().Contain("ColumnsDoNotFit");
    }

    [Fact]
    public void A_preferred_column_takes_no_width_past_the_maximums_beside_an_auto_column()
    {
        // narrow-f: the 90pt column keeps 90pt in the 100% table; the auto column takes the rest.
        var page = Paginate(Document("narrow-f")).Single();

        Boxes(page.Root.Fragment, SemanticRole.TableCell).First().Size.Width.Should().BeApproximately(90, 1e-9);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// A table's caption (<see cref="TableDisplay.Caption"/>, CSS 2.1 §17.4), laid out by its table as Chromium 159 prints it.
/// The caption is a block as wide as the table's border box, its margins inside the table's outer box and not collapsing
/// with the table's own margins (cases <c>tc-side</c>, <c>tc-box</c>): above the grid on the table's first page, or below
/// it on the table's last page (<see cref="BoxStyle.CaptionSide"/>; cases <c>tc-break-rows</c>, <c>tc-break-bottom</c>:
/// it is not repeated on the pages between). The table's own border, padding and background are the grid's, not the
/// caption's (CSS 2.1 §17.4: the caption lies outside the table box, inside the table wrapper box), so they are drawn
/// around the grid alone (<see cref="Wrap"/>). The caption's min-content width, with its margins, is the least width of the
/// table: a longer word widens the table, while a longer text only wraps (<c>tc-wrap</c>, <see cref="MinWidth"/>).
/// </summary>
/// <remarks>
/// At a break the caption is refused where no case shows how Chromium prints it (<see cref="LayoutErrors.CaptionBreak"/>):
/// a caption at the top that fits on the page while the first body row below it does not (case <c>tc-break-first</c>: with
/// collapsed borders Chromium leaves the caption alone at the end of the page and moves the grid, with separated borders it
/// moves both), and a caption at the bottom that does not fit below the table's last row.
/// </remarks>
internal static class TableCaption
{
    /// <summary>The caption of a tracks box: its first child, when that is a <see cref="TableDisplay.Caption"/> box; else
    /// null.</summary>
    /// <param name="table">The tracks box.</param>
    /// <returns>The caption, or null.</returns>
    public static Box? Of(Box table) =>
        table.Children.Count > 0 && table.Children[0] is Box { Style.TableDisplay: TableDisplay.Caption } caption ? caption : null;

    /// <summary>The least width a caption gives its table: its min-content width with its horizontal margins (cases
    /// <c>tc-wrap</c> b, c: a word of 173.391 pt makes a table of two short columns 173.402 pt wide, 1/64 px more, and its
    /// columns share the width as they share any width above their maximum).</summary>
    /// <param name="environment">The fonts, algorithms and current page.</param>
    /// <param name="caption">The caption.</param>
    /// <returns>The width, or an error.</returns>
    public static Result<double, Error> MinWidth(LayoutEnvironment environment, Box caption) =>
        environment.Measure(caption).OnSuccessMap(widths => widths.MinContent + caption.Style.Margin.Left.Points + caption.Style.Margin.Right.Points);

    /// <summary>
    /// A page of a table with a caption: the grid laid out by <paramref name="grid"/> in the room the caption leaves, and
    /// the caption above it on the first page or below it on the last.
    /// </summary>
    /// <param name="environment">The fonts, algorithms and current page.</param>
    /// <param name="table">The tracks box.</param>
    /// <param name="caption">Its caption.</param>
    /// <param name="width">The table's border-box width.</param>
    /// <param name="headerRows">The number of header rows: the first body row comes after them.</param>
    /// <param name="space">The table's space on this page.</param>
    /// <param name="continues">True on a page after the table's first.</param>
    /// <param name="grid">Lays the grid out in a space.</param>
    /// <returns>The table's page, NoRoom, or an error.</returns>
    public static Result<LayoutResult, Error> Layout(
        LayoutEnvironment environment, Box table, Box caption, double width, int headerRows, ConstraintSpace space, bool continues, Func<ConstraintSpace, Result<LayoutResult, Error>> grid)
    {
        if (caption.Style.CaptionSide == CaptionSide.Bottom)
        {
            return grid(space).OnSuccessBind(result => result is LayoutResult.Placed { Continuation: null } last
                ? Placed(environment, caption, width, space.AvailableHeight - last.Fragment.Size.Height, space.FragmentainerBlockSize).OnSuccessBind(below => below is { } placed
                    ? Result<LayoutResult, Error>.Success(last with { Fragment = Wrap(table, last.Fragment, placed.Fragment, placed.Height, isTop: false) })
                    : Result<LayoutResult, Error>.Failure(LayoutErrors.CaptionBreak(table, caption, space.AvailableHeight, "below the table's last row")))
                : Result<LayoutResult, Error>.Success(result));
        }

        if (continues)
        {
            return grid(space);
        }

        return Placed(environment, caption, width, space.AvailableHeight, space.FragmentainerBlockSize).OnSuccessBind(above => above is not { } placed
            ? Refused(table, caption, space)
            : grid(space with { AvailableHeight = space.AvailableHeight - placed.Height, IsAtPageStart = false, IsBelowStartInsets = false, FollowsContent = true })
                .OnSuccessBind(result => result is LayoutResult.Placed rows && FirstRowPlaced(rows, headerRows)
                    ? Result<LayoutResult, Error>.Success(rows with { Fragment = Wrap(table, rows.Fragment, placed.Fragment, placed.Height, isTop: true) })
                    : Refused(table, caption, space)));
    }

    /// <summary>The refusal of a caption at the top that does not fit on the page with its table's first body row below
    /// it whole: Chromium leaves the caption alone or moves it with the table, by a rule no two cases settle
    /// (<c>tc-break-first</c> a, b).</summary>
    private static Result<LayoutResult, Error> Refused(Box table, Box caption, ConstraintSpace space) =>
        Result<LayoutResult, Error>.Failure(LayoutErrors.CaptionBreak(table, caption, space.AvailableHeight, "above the table's first row"));

    /// <summary>True when the page holds the table's first body row whole: the table ends on the page, or continues at a
    /// later row.</summary>
    private static bool FirstRowPlaced(LayoutResult.Placed rows, int headerRows) =>
        rows.Continuation is not TracksBreakToken next || next.NextRow > headerRows;

    /// <summary>The caption laid out at the table's width in <paramref name="room"/>: its fragment placed inside its margins
    /// and its height with them, or null when it does not fit there whole (a caption is never broken over pages).</summary>
    private static Result<(PlacedFragment Fragment, double Height)?, Error> Placed(LayoutEnvironment environment, Box caption, double width, double room, double fragmentainer)
    {
        var margin = caption.Style.Margin;
        var space = new ConstraintSpace(width - margin.Left.Points - margin.Right.Points, double.PositiveInfinity, IsAtPageStart: false, IsNewFormattingContext: true)
        {
            FragmentainerBlockSize = fragmentainer,
        };
        return environment.Layout(caption, space, null).OnSuccessMap(result =>
        {
            if (result is not LayoutResult.Placed { Continuation: null } placed)
            {
                return ((PlacedFragment, double)?)null;
            }

            var height = margin.Top.Points + placed.Fragment.Size.Height + margin.Bottom.Points;
            return Fitting.Fits(height, room) ? (new PlacedFragment(new Point(margin.Left.Points, margin.Top.Points), placed.Fragment), height) : null;
        });
    }

    /// <summary>
    /// The table's fragment on a page with its caption: the caption, then the grid moved below it (or the grid, then the
    /// caption below it), in a fragment of the table that draws no background or border of its own. The grid's background
    /// and its border sides on this page are drawn as filled rectangles of the grid's size, as the painter draws a box's
    /// (<see cref="RuleFragment"/>, layout decoration), so they lie around the grid alone; the table's structure holds the
    /// caption and the rows.
    /// </summary>
    private static BoxFragment Wrap(Box table, BoxFragment grid, PlacedFragment caption, double captionHeight, bool isTop)
    {
        var top = isTop ? captionHeight : 0;
        IEnumerable<PlacedFragment> children =
        [
            .. isTop ? [caption] : Array.Empty<PlacedFragment>(),
            .. Decoration(table, grid).Select(item => item with { Offset = new Point(item.Offset.X, item.Offset.Y + top) }),
            .. grid.Children.Select(child => child with { Offset = new Point(child.Offset.X, child.Offset.Y + top) }),
            .. isTop ? Array.Empty<PlacedFragment>() : [caption with { Offset = new Point(caption.Offset.X, caption.Offset.Y + grid.Size.Height) }],
        ];
        return new BoxFragment(table with { Style = table.Style with { Background = null } }, new Size(grid.Size.Width, grid.Size.Height + captionHeight), [.. children], IsRepeated: grid.IsRepeated);
    }

    /// <summary>The grid fragment's background and visible border sides as filled rectangles: the top and bottom sides the
    /// full width, the left and right sides between them, as <see cref="PagePainter"/> draws a box's border.</summary>
    private static IEnumerable<PlacedFragment> Decoration(Box table, BoxFragment grid)
    {
        var (border, size) = (grid.Border, grid.Size);
        if (table.Style.Background is { } background)
        {
            yield return new PlacedFragment(new Point(0, 0), new RuleFragment(background, size));
        }

        var (top, bottom) = (border.Top.Width.Points, border.Bottom.Width.Points);
        (BorderSide Side, Point At, Size Size)[] sides =
        [
            (border.Top, new Point(0, 0), new Size(size.Width, top)),
            (border.Bottom, new Point(0, size.Height - bottom), new Size(size.Width, bottom)),
            (border.Left, new Point(0, top), new Size(border.Left.Width.Points, size.Height - top - bottom)),
            (border.Right, new Point(size.Width - border.Right.Width.Points, top), new Size(border.Right.Width.Points, size.Height - top - bottom)),
        ];
        foreach (var (side, at, area) in sides.Where(side => side.Side.IsVisible))
        {
            yield return new PlacedFragment(at, new RuleFragment(side.Color, area));
        }
    }
}

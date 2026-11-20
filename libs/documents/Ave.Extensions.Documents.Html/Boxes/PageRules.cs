using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Cascade;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Html.Boxes;

/// <summary>
/// The page box of the document from its <c>@page</c> rules (CSS Paged Media 3 §4–§7): the rules without a page
/// selector, in cascade order (later wins, important over normal), give the page size (§7.1, <c>size</c>) and the page
/// margins (§4.2 and CSS Box Model 3 §5.1). Another declaration, a value that is not an absolute length of 0 or more
/// (a percentage, a relative length, a negative one), and the size and margins of an <c>@page :first</c> rule are
/// refused at their place; the page is then made from what is left. The page-margin rules and the rest of a
/// <c>:first</c> rule are the page-context cascade's (<see cref="Css.Cascade.PageContextCascade"/>), which also refuses
/// the rules with any other selector; they are read for the margin boxes (<see cref="HtmlBoxGenerator"/>).
/// </summary>
internal sealed class PageRules(string sourceName, List<Error> refusals)
{
    /// <summary>The page sizes of CSS Paged Media 3 §7.1.1, portrait, in points.</summary>
    private static readonly Dictionary<string, (double Width, double Height)> Sizes = new()
    {
        ["a5"] = (148 * Mm, 210 * Mm),
        ["a4"] = (210 * Mm, 297 * Mm),
        ["a3"] = (297 * Mm, 420 * Mm),
        ["b5"] = (176 * Mm, 250 * Mm),
        ["b4"] = (250 * Mm, 353 * Mm),
        ["jis-b5"] = (182 * Mm, 257 * Mm),
        ["jis-b4"] = (257 * Mm, 364 * Mm),
        ["letter"] = (612, 792),
        ["legal"] = (612, 1008),
        ["ledger"] = (792, 1224),
    };

    private const double Mm = 72 / 25.4;

    /// <summary>The page the rules make from a default page.</summary>
    /// <param name="rules">The <c>@page</c> rules that apply, in order.</param>
    /// <param name="page">The page without rules.</param>
    /// <returns>The page.</returns>
    public PageStyle Apply(ValueList<CascadedPageRule> rules, PageStyle page)
    {
        var declarations = new List<Declaration>();
        foreach (var cascaded in rules)
        {
            var rule = cascaded.Rule;
            if (rule.Selectors is [{ Name: null, PseudoClasses: ["first"] }])
            {
                foreach (var declaration in rule.Declarations.Where(declaration => declaration.Name == "size" || declaration.Name.StartsWith("margin", System.StringComparison.Ordinal)))
                {
                    refusals.Add(new Error(HtmlStyleErrorCodes.Unsupported.PageRule, $"'{declaration.Name}' in an @page :first rule is not drawn in this version: every page takes the size and the margins of the @page rules without a selector.")
                        .With(ErrorMetadataKeys.Value, declaration.Name).WithSource(sourceName, declaration.Position));
                }
            }

            if (rule.Selectors.Count == 0)
            {
                declarations.AddRange(rule.Declarations);
            }
        }

        var (width, height) = (page.Width, page.Height);
        var margins = page.Margins;
        foreach (var declaration in declarations.Where(d => !d.Important).Concat(declarations.Where(d => d.Important)))
        {
            var values = declaration.Value.Where(value => value is not CssToken { Kind: CssTokenKind.Whitespace }).ToList();
            switch (declaration.Name)
            {
                case "size" when Size(values, page) is { } size:
                    (width, height) = size;
                    break;
                case "margin" when values.Count is >= 1 and <= 4 && values.Select(Points).ToList() is var sides && sides.All(side => side is not null):
                    var (top, right, bottom, left) = values.Count switch
                    {
                        1 => (sides[0], sides[0], sides[0], sides[0]),
                        2 => (sides[0], sides[1], sides[0], sides[1]),
                        3 => (sides[0], sides[1], sides[2], sides[1]),
                        _ => (sides[0], sides[1], sides[2], sides[3]),
                    };
                    margins = new Edges(top!.Value, right!.Value, bottom!.Value, left!.Value);
                    break;
                case "margin-top" when values.Count == 1 && Points(values[0]) is { } topSide:
                    margins = margins with { Top = topSide };
                    break;
                case "margin-right" when values.Count == 1 && Points(values[0]) is { } rightSide:
                    margins = margins with { Right = rightSide };
                    break;
                case "margin-bottom" when values.Count == 1 && Points(values[0]) is { } bottomSide:
                    margins = margins with { Bottom = bottomSide };
                    break;
                case "margin-left" when values.Count == 1 && Points(values[0]) is { } leftSide:
                    margins = margins with { Left = leftSide };
                    break;
                case var name when HasNoEffect(name):
                    break;
                default:
                    Refuse($"'{declaration.Name}' with this value in an @page rule", declaration.Name, declaration.Position);
                    break;
            }
        }

        var pixels = Pixels(width, height, margins);
        var cut = Length.FromPoints(System.Math.Truncate(margins.Left.Points / 0.75 * 64) / 64 * 0.75);
        RowStart = cut.Points < pixels.Margins.Left.Points ? cut : pixels.Margins.Left;
        return pixels;
    }

    /// <summary>
    /// After <see cref="Apply"/>: where Chromium 159 starts the rows of page-margin boxes, the smaller of the left page
    /// margin cut to 1/64 CSS px and the page area's start, that margin rounded to a whole pixel (<see cref="Pixels"/>).
    /// Measured: a 50 pt margin (66.65625 px) starts the rows at 49.992 pt and the page area at 50.25 pt (cases
    /// <c>pm-width</c>, <c>pm-defaults</c>), an A4 margin of 1 cm at 28.336 pt where the page area starts at 28.5 pt (the
    /// timesheets); margins that round down start both at the page area's start: 25 mm (94.488 px) at 70.5 pt, 12.2 pt
    /// (16.267 px) at 12.0 pt (cases <c>pm-start-25mm</c>, <c>pm-start-12pt</c>).
    /// </summary>
    public Length RowStart { get; private set; }

    /// <summary>
    /// The page as Chromium 159 lays it out and prints it. Blink lays the page out in CSS pixels held in units of 1/64
    /// pixel: the page box is the size rounded to 1/64 px (<c>DesiredPageContainingBlockSize</c>, pagination_utils.cc),
    /// a margin is cut to 1/64 px (a fixed length made a <c>LayoutUnit</c>). The page area starts at the left and top
    /// margins rounded to a whole pixel, half up, and its size is what the margins leave, rounded up to a whole pixel
    /// (<c>SnappedBorderBoxRect</c> in page_container_layout_algorithm.cc;
    /// <c>PageBorderBoxLayoutAlgorithm::CreateConstraintSpaceForPageArea</c> in page_border_box_layout_algorithm.cc).
    /// The sheet is the page box in points, rounded twice (<see cref="Printed"/>). The engine's right and bottom margins
    /// are what the sheet leaves beside that page area; where the area reaches past the sheet (no margin, a size that is
    /// not whole pixels) they are 0 and the engine's page area is the smaller by less than a pixel: a listed difference
    /// (measured on A4 without margins, cases <c>h-page-a4-zero-*</c>; 1 px margins remove it, <c>h-page-a4-margin-px</c>).
    /// Measured: cases
    /// <c>h-page-margin-round</c> (11.75 pt margins: area from 16 px to 385 px), <c>h-page-margin-half</c> (15.5 px
    /// margins: left at 16 px), <c>h-page-area-half</c> (368.5 px left: 369 px), <c>h-page-area-right</c>,
    /// <c>h-page-area-justify</c> (right edges), <c>h-page-area-bottom</c> (29 lines of 12.89 px fit 374 px).
    /// </summary>
    private static PageStyle Pixels(Length width, Length height, Edges margins)
    {
        static double Px(Length length) => length.Points / 0.75;
        static double Rounded(double px) => System.Math.Round(px * 64, System.MidpointRounding.AwayFromZero) / 64;
        static double Cut(double px) => System.Math.Truncate(px * 64) / 64;
        var (boxWidth, boxHeight) = (Rounded(Px(width)), Rounded(Px(height)));
        var (top, right, bottom, left) = (Cut(Px(margins.Top)), Cut(Px(margins.Right)), Cut(Px(margins.Bottom)), Cut(Px(margins.Left)));
        var (x, y) = (System.Math.Floor(left + 0.5), System.Math.Floor(top + 0.5));
        var (areaWidth, areaHeight) = (System.Math.Ceiling(boxWidth - left - right), System.Math.Ceiling(boxHeight - top - bottom));
        var (sheetWidth, sheetHeight) = (Printed(boxWidth * 0.75), Printed(boxHeight * 0.75));
        static Length Points(double points) => Length.FromPoints(System.Math.Max(0, points));
        return new PageStyle(sheetWidth, sheetHeight, new Edges(
            Points(y * 0.75),
            Points(sheetWidth.Points - ((x + areaWidth) * 0.75)),
            Points(sheetHeight.Points - ((y + areaHeight) * 0.75)),
            Points(x * 0.75)));
    }

    /// <summary>
    /// A side of the page box as Chromium 159 prints it: rounded to whole points (<c>gfx::ToRoundedSize</c> in
    /// <c>PrintRenderFrameHelper::PrintPageInternal</c>, print_render_frame_helper.cc), then to whole 1/300 inch
    /// (<c>SkPDFDocument::onBeginPage</c> rounds the page to device pixels at the raster resolution of 300 dpi that
    /// <c>MakePdfDocument</c>, printing/common/metafile_utils.cc, sets; the media box is those pixels again in points).
    /// Measured: A4 (595.28 pt) prints 594.96, A5's 419.53 pt prints 420 (1/300 inch alone gives 419.52), 595.5 pt prints
    /// 595.92 (case <c>h-page-size-fraction</c>), 100.5 px (75.375 pt) prints 75.12 (case <c>h-page-size-hundred-px</c>).
    /// </summary>
    /// <param name="points">The side in points, from the page box in units of 1/64 px.</param>
    internal static Length Printed(double points) =>
        Length.FromPoints(System.Math.Round(System.Math.Round(points, System.MidpointRounding.AwayFromZero) * 300 / 72, System.MidpointRounding.AwayFromZero) * 72 / 300);

    /// <summary>
    /// Declarations in an <c>@page</c> rule that are not the page box's: the inherited properties (they reach only the
    /// margin boxes, CSS Paged Media 3 §5.3) and the page counters' changes, both checked by the page-context cascade.
    /// Every other declaration but the size and the margins is refused, also one the registry does not know: CSS reads
    /// an @page rule's declarations unchecked (stage 3 keeps them as written).
    /// </summary>
    internal static bool HasNoEffect(string name) =>
        name is "counter-reset" or "counter-increment" or "counter-set"
        || (Css.Properties.PropertyRegistry.Find(name) is { Inherited: true })
        || (Css.Properties.PropertyRegistry.FindShorthand(name) is { } shorthand && shorthand.Longhands.All(longhand => Css.Properties.PropertyRegistry.Find(longhand) is { Inherited: true }));

    /// <summary>The value of <c>size</c> (§7.1): auto, one or two lengths, a page size with or without an orientation, or
    /// an orientation alone; null for anything else. <c>auto</c> is the size of the page without rules, an orientation
    /// alone turns that page if it has the other one (<c>DesiredPageContainingBlockSize</c>, pagination_utils.cc;
    /// measured on US Letter, cases <c>h-page-size-auto</c>, <c>h-page-landscape</c>, <c>h-page-portrait</c>).</summary>
    private static (Length Width, Length Height)? Size(List<ComponentValue> values, PageStyle page)
    {
        if (values is [CssToken { Kind: CssTokenKind.Ident } auto] && InfraAscii.EqualsIgnoreCase(auto.Text, "auto"))
        {
            return (page.Width, page.Height);
        }

        var lengths = values.Select(Points).ToList();
        if (lengths.Count is 1 or 2 && lengths.All(length => length is { Points: > 0 }))
        {
            return (lengths[0]!.Value, lengths[^1]!.Value);
        }

        (double Width, double Height)? named = null;
        bool? landscape = null;
        foreach (var value in values)
        {
            if (value is not CssToken { Kind: CssTokenKind.Ident } ident)
            {
                return null;
            }

            var word = InfraAscii.ToLower(ident.Text);
            if (word is "portrait" or "landscape" && landscape is null)
            {
                landscape = word == "landscape";
            }
            else if (Sizes.TryGetValue(word, out var size) && named is null)
            {
                named = size;
            }
            else
            {
                return null;
            }
        }

        if (named is not { } paper)
        {
            var (shorter, longer) = page.Width.Points <= page.Height.Points ? (page.Width, page.Height) : (page.Height, page.Width);
            return landscape == true ? (longer, shorter) : (shorter, longer);
        }

        return landscape == true ? (Length.FromPoints(paper.Height), Length.FromPoints(paper.Width)) : (Length.FromPoints(paper.Width), Length.FromPoints(paper.Height));
    }

    /// <summary>An absolute length of 0 or more (or the number 0), in points; null for anything else.</summary>
    private static Length? Points(ComponentValue value) => value switch
    {
        CssToken { Kind: CssTokenKind.Number, Number: 0 } => Length.Zero,
        CssToken { Kind: CssTokenKind.Dimension, Number: >= 0 } dimension when PointsPer(dimension.Text) is { } perUnit =>
            Length.FromPoints(dimension.Number * perUnit),
        _ => null,
    };

    /// <summary>Points per absolute unit (CSS Values 4 §6.2); null for a relative unit.</summary>
    private static double? PointsPer(string unit) => InfraAscii.ToLower(unit) switch
    {
        "pt" => 1,
        "px" => 0.75,
        "pc" => 12,
        "in" => 72,
        "cm" => 72 / 2.54,
        "mm" => Mm,
        "q" => Mm / 4,
        _ => null,
    };

    private void Refuse(string what, string value, TextPosition position) =>
        refusals.Add(new Error(HtmlStyleErrorCodes.Unsupported.PageRule, $"{char.ToUpperInvariant(what[0])}{what[1..]} is not drawn in this version: only the size and the margins of @page rules without a selector are.")
            .With(ErrorMetadataKeys.Value, value).WithSource(sourceName, position));
}

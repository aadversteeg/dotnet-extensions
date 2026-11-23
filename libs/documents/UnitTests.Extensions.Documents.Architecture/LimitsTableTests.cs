using System.IO;
using System.Reflection;

namespace UnitTests.Extensions.Documents.Architecture;

/// <summary>
/// The table of limits in docs/architecture.md ("Bounded work") against the constants that enforce them: every row
/// of the table is listed here with the text of its value cell, and every constant behind the row has the value the
/// cell states. A row added, removed or reworded, or a constant changed, fails until both say the same.
/// </summary>
public class LimitsTableTests
{
    private sealed record Constant(string Assembly, string Type, string Member, double Value);

    private static Constant C(string assembly, string type, string member, double value) =>
        new($"Ave.Extensions.Documents.{assembly}", $"Ave.Extensions.Documents.{type}", member, value);

    private static readonly Dictionary<string, (string Value, Constant[] Constants)> Rows = new(StringComparer.Ordinal)
    {
        ["work of a layout table parse"] = ("8 units per byte, at least 16 384",
            [C("Fonts", "Fonts.OpenType.TableReader", "WorkPerByte", 8), C("Fonts", "Fonts.OpenType.TableReader", "MinimumWork", 16_384)]),
        ["decoded image (PNG, and JPEG decoded to samples)"] = ("2^26 pixels", [C("Images", "Images.ImageLimits", "MaxPixels", 1L << 26)]),
        ["blocks the scans of one JPEG visit together"] = ("32 times the blocks of its frame", [C("Images", "Images.Jpeg.JpegDecoder", "MaxPasses", 32)]),
        ["glyphs of one text"] = ("64 per input glyph, at least 16 384",
            [C("Typesetting", "Typesetting.Shaping.GlyphSubstitution", "MaxGrowthFactor", 64), C("Typesetting", "Typesetting.Shaping.GlyphSubstitution", "MinGlyphLimit", 16_384)]),
        ["shaping operations of one text"] = ("1 024 per input glyph, at least 65 536",
            [C("Typesetting", "Typesetting.Shaping.ShapingBudget", "OperationsPerGlyph", 1_024), C("Typesetting", "Typesetting.Shaping.ShapingBudget", "MinOperations", 65_536)]),
        ["cell span"] = ("1 000 columns, 65 534 rows", [C("Core", "Core.TableSlots", "MaxColumnSpan", 1_000), C("Core", "Core.TableSlots", "MaxRowSpan", 65_534)]),
        ["every number of a page model (sizes, positions, font sizes, areas), and the edges computed from them (x + width, y + height, the place of a glyph, the height above the foot of the page)"] = ("within ±3.403 × 10^38, the range of a PDF real (ISO 32000-1 Annex C, Table C.1)",
            [C("PageModel", "PageModel.PageModelRules", "MaxNumber", 3.403e38)]),
        ["clips open one inside the other on a page, for both back-ends"] = ("27 (in PDF one `q` each, plus the drawing's own: the 28 levels of ISO 32000-1 Annex C and ISO 19005-3 §6.1.13; the raster back-end takes the same)",
            [C("PageModel", "PageModel.PageModelRules", "MaxClipDepth", 27)]),
        ["slices of one piece of content taller than the page area (`MonolithicOverflow.Slice`)"] = ("100, one per page", [C("Layout", "Layout.MonolithicSlices", "MaxSlices", 100)]),
        ["slices of one document, all pieces together (`MonolithicOverflow.Slice`)"] = ("1 000, counted page by page; a document without a piece taller than the page area has none",
            [C("Layout", "Layout.MonolithicSlices", "MaxSlicesPerDocument", 1_000)]),
        ["pixels of one page drawn as an image"] = ("2^26", [C("Graphics", "Graphics.Canvas", "MaxPixels", 1L << 26)]),
        ["coordinates the graphics library draws with"] = ("±10^150 after the transform; a clip box within ±2^30 pixels",
            [C("Graphics", "Graphics.Canvas", "MaxCoordinate", 1e150), C("Graphics", "Graphics.Canvas", "MaxPixelCoordinate", 1 << 30)]),
        ["PDF/UA grid of one table"] = ("the first row's width; 4 194 304 slots", [C("Render.Pdf", "Render.Pdf.Tagging.TableRegularity", "MaxGridSlots", 4_194_304)]),
        ["nesting of elements"] = ("64 levels", [C("Fluent", "Fluent.InputLimits", "MaxNestingDepth", 64)]),
        ["a link's URI"] = ("32 767 characters of printable ASCII (the longest string a PDF/A file may hold, veraPDF rule 6.1.13-3)", [C("Core", "Core.LinkUris", "MaxLength", 32_767)]),
        ["link areas on one page"] = ("8 191, a fixed bound on the annotations one page writes (one object and one `/Annots` entry each), far more than a page of text can show", [C("PageModel", "PageModel.PageModelRules", "MaxLinkAreasPerPage", 8_191)]),
        ["nesting of arrays and dictionaries in one PDF object"] = ("64 levels", [C("Pdf", "Pdf.PdfSerializer", "MaxNestingDepth", 64)]),
        ["nesting of arrays and structures in one XMP property"] = ("64 levels", [C("Pdf", "Pdf.Xmp.XmpPacket", "MaxNestingDepth", 64)]),
        ["a length in the front-end"] = ("14 400 pt", [C("Fluent", "Fluent.InputLimits", "MaxLength", 14_400)]),
        ["an HTML document"] = ("2^24 UTF-16 code units (bytes are checked against the same number); nesting 512 elements, the depth at which Chromium stops nesting; 2^18 elements and 2^20 nodes made, the elements the parser opens again or clones included; selectedcontent clones 4 nodes and characters per input character, at least 65 536; 10 000 diagnostics kept",
            [C("Html.Parsing", "Html.Parsing.HtmlInputLimits", "MaxCharacters", 1 << 24), C("Html.Parsing", "Html.Parsing.HtmlInputLimits", "MaxDepth", 512),
             C("Html.Parsing", "Html.Parsing.HtmlInputLimits", "MaxElements", 1 << 18), C("Html.Parsing", "Html.Parsing.HtmlInputLimits", "MaxNodes", 1 << 20),
             C("Html.Parsing", "Html.Parsing.TreeBuilding.TreeBuilder", "CloneWorkPerCharacter", 4), C("Html.Parsing", "Html.Parsing.TreeBuilding.TreeBuilder", "MinimumCloneWork", 65_536),
             C("Html.Parsing", "Html.Parsing.HtmlInputLimits", "MaxDiagnostics", 10_000)]),
        ["a style sheet"] = ("2^24 UTF-16 code units; as bytes, 3 × 2^24 before decoding (UTF-8 takes at most 3 bytes per code unit), then 2^24 code units; nesting of blocks, functions and rules 64",
            [C("Css", "Css.CssInputLimits", "MaxCharacters", 1 << 24), C("Css", "Css.CssParser", "MaxBytesPerCharacter", 3), C("Css", "Css.CssInputLimits", "MaxDepth", 64)]),
        ["nesting of selectors (pseudo-class arguments and nested style rules together)"] = ("32 levels below the top one (a style rule and 32 nested in it)", [C("Css", "Css.CssInputLimits", "MaxSelectorNesting", 32)]),
        ["work of one cascade"] = ("2^26 units: a matching fact computed, a simple selector tested, 64 characters compared, a sibling counted, a rule or declaration collected, a candidate looked at, a comparison of a sort, a var() reference followed",
            [C("Css", "Css.CssInputLimits", "MaxCascadeWork", 1L << 26), C("Css", "Css.Cascade.CascadeBudget", "CharactersPerUnit", 64)]),
        ["entries one cascade remembers"] = ("2^22: matching facts, parent-rule matches, sibling tables, var() substitutions, custom-property tables; past it they are computed again, inside the work limit",
            [C("Css", "Css.CssInputLimits", "MaxCascadeMemo", 1 << 22)]),
        ["entries of one cascade's index"] = ("2^21: a selector of a style rule or a declaration, counted each time a sheet is taken in", [C("Css", "Css.CssInputLimits", "MaxCascadeEntries", 1 << 21)]),
        ["style sheets of one cascade"] = ("1 024: the user-agent sheet, style elements, linked and imported sheets; a sheet taken in twice counts twice", [C("Css", "Css.CssInputLimits", "MaxStyleSheets", 1_024)]),
        ["one var() substitution"] = ("2 097 152 characters of text, Chromium 159's limit (measured); within 8 characters per var() of it the engine cannot tell what Chromium keeps (refused); the value it makes nests at most 64 levels",
            [C("Css", "Css.CssInputLimits", "MaxSubstitutionLength", 2_097_152), C("Css", "Css.Cascade.VarSubstitution", "UncertainCharactersPerSubstitution", 8), C("Css", "Css.CssInputLimits", "MaxDepth", 64)]),
        ["all var() substitutions of one cascade"] = ("2^23 characters", [C("Css", "Css.CssInputLimits", "MaxSubstitutionWork", 1L << 23)]),
        ["a computed length or percentage"] = ("2^24 px (or percent) either way; Chromium clamps near 3.36 × 10^7 px", [C("Css", "Css.CssInputLimits", "MaxLengthPixels", 1 << 24)]),
        ["diagnostics of one style sheet, one cascade, one resolution"] = ("10 000 kept; each quotes at most 100 characters of one name or value from the input, with `…` where it is cut",
            [C("Css", "Css.CssInputLimits", "MaxDiagnostics", 10_000), C("Html.Parsing", "Html.Parsing.HtmlInputLimits", "MaxDiagnostics", 10_000),
             C("Css", "InputQuote", "MaxCharacters", 100), C("Html", "InputQuote", "MaxCharacters", 100),
             C("Html.Parsing", "InputQuote", "MaxCharacters", 100), C("Css", "Css.Syntax.CssSerializer", "MaxQuotedCharacters", 100)]),
        ["points of one glyph outline, components included"] = ("65 536", [C("Fonts", "Fonts.Tables.GlyphOutlineDecoder", "MaxPoints", 65_536)]),
        ["component references one glyph outline visits"] = ("65 536", [C("Fonts", "Fonts.Tables.GlyphOutlineDecoder", "MaxComponents", 65_536)]),
        ["nesting of composite glyphs"] = ("64 levels", [C("Fonts", "Fonts.Tables.GlyphOutlineDecoder", "MaxDepth", 64)]),
    };

    /// <summary>Per row, the "Where" and "Error" cells: where the limit is enforced and what reaching it gives.</summary>
    private static readonly Dictionary<string, (string Where, string Error)> Places = new(StringComparer.Ordinal)
    {
        ["work of a layout table parse"] = ("`Fonts/OpenType/TableReader.cs`", "`Documents.Font.Parse.InvalidValue`"),
        ["decoded image (PNG, and JPEG decoded to samples)"] = ("`Images/ImageLimits.cs`", "`Documents.Image.TooLarge`"),
        ["blocks the scans of one JPEG visit together"] = ("`Images/Jpeg/JpegDecoder.cs`", "`Documents.Image.TooMuchWork`"),
        ["glyphs of one text"] = ("`Typesetting/Shaping/GlyphSubstitution.cs`", "`Documents.Typesetting.TooManyGlyphs`"),
        ["shaping operations of one text"] = ("`Typesetting/Shaping/ShapingBudget.cs`", "`Documents.Typesetting.TooMuchWork`"),
        ["cell span"] = ("`Core/TableSlots.cs`", "`Documents.Input.Invalid.Table`; in a page model the back-end's code, `Documents.Pdf.Write.InvalidPageModel` or `Documents.Raster.InvalidPageModel`"),
        ["every number of a page model (sizes, positions, font sizes, areas), and the edges computed from them (x + width, y + height, the place of a glyph, the height above the foot of the page)"] = ("`PageModel/PageModelRules.cs`", "`Documents.Pdf.Write.InvalidPageModel` or `Documents.Raster.InvalidPageModel`"),
        ["pixels of one page drawn as an image"] = ("`Render.Raster/PageRasterizer.cs`, `Graphics/Canvas.cs`", "`Documents.Raster.TooLarge`; `Documents.Graphics.TooLarge`"),
        ["coordinates the graphics library draws with"] = ("`Graphics/Canvas.cs`", "`Documents.Graphics.InvalidValue`"),
        ["PDF/UA grid of one table"] = ("`Render.Pdf/Tagging/TableRegularity.cs`", "`Documents.Pdf.Conformance.PdfUa1.TableTooLarge`"),
        ["nesting of elements"] = ("`Fluent/InputLimits.cs`", "`Documents.Input.Invalid.TooDeep`"),
        ["a link's URI"] = ("`Core/LinkUris.cs`", "`Documents.Input.Invalid.Link`, from the front-end and the model check; in a page model the back-end's code, `Documents.Pdf.Write.InvalidPageModel` or `Documents.Raster.InvalidPageModel`"),
        ["link areas on one page"] = ("`PageModel/PageModelRules.cs`", "`Documents.Pdf.Write.InvalidPageModel` or `Documents.Raster.InvalidPageModel`"),
        ["nesting of arrays and dictionaries in one PDF object"] = ("`Pdf/PdfSerializer.cs`", "`Documents.Pdf.Write.NestedTooDeep`"),
        ["nesting of arrays and structures in one XMP property"] = ("`Pdf/Xmp/XmpPacket.cs`", "`Documents.Pdf.Write.NestedTooDeep`"),
        ["a length in the front-end"] = ("`Fluent/InputLimits.cs`", "the property's `Documents.Input.Invalid.*` code"),
        ["an HTML document"] = ("`Html.Parsing/HtmlInputLimits.cs`, `Html.Parsing/TreeBuilding/TreeBuilder.Select.cs`", "`Documents.Html.TooLarge`, `TooDeep` (the parse fails); `Documents.TooManyDiagnostics` (the last diagnostic)"),
        ["a style sheet"] = ("`Css/CssInputLimits.cs`", "`Documents.Css.TooLarge`, `TooDeep` (the reading fails; a style resolution fails with it)"),
        ["nesting of selectors (pseudo-class arguments and nested style rules together)"] = ("`Css/CssInputLimits.cs`", "`Documents.Css.TooDeep` (the style resolution fails)"),
        ["work of one cascade"] = ("`Css/CssInputLimits.cs` (`MaxCascadeWork`)", "`Documents.Css.TooLarge` (the style resolution fails)"),
        ["entries one cascade remembers"] = ("`Css/CssInputLimits.cs` (`MaxCascadeMemo`)", "none"),
        ["entries of one cascade's index"] = ("`Css/CssInputLimits.cs` (`MaxCascadeEntries`)", "`Documents.Css.TooLarge` (the style resolution fails)"),
        ["style sheets of one cascade"] = ("`Css/CssInputLimits.cs` (`MaxStyleSheets`)", "`Documents.Css.TooLarge` (the style resolution fails)"),
        ["one var() substitution"] = ("`Css/CssInputLimits.cs` (`MaxSubstitutionLength`, `MaxDepth`)", "past the length: invalid at computed-value time, as in Chromium (`Documents.Css.Invalid.ComputedValue`); within 8 characters per substitution of it, where the engine's measure cannot tell: refused (`Documents.Css.Unsupported.Value`); deeper: `Documents.Css.TooDeep` (the style resolution fails)"),
        ["all var() substitutions of one cascade"] = ("`Css/CssInputLimits.cs` (`MaxSubstitutionWork`)", "`Documents.Css.TooLarge` (the style resolution fails)"),
        ["a computed length or percentage"] = ("`Css/CssInputLimits.cs` (`MaxLengthPixels`)", "`Documents.Css.Unsupported.Value` (the declaration is refused)"),
        ["diagnostics of one style sheet, one cascade, one resolution"] = ("`Css/CssInputLimits.cs`, `Html.Parsing/HtmlInputLimits.cs`, `Shared/InputQuote.cs` (compiled into Css, Html.Parsing and Html)", "`Documents.TooManyDiagnostics`: how many were left out, and how many of those refuse the document"),
        ["points of one glyph outline, components included"] = ("`Fonts/Tables/GlyphOutlineDecoder.cs`", "`Documents.Font.Parse.InvalidValue`"),
        ["component references one glyph outline visits"] = ("`Fonts/Tables/GlyphOutlineDecoder.cs`", "`Documents.Font.Parse.InvalidValue`"),
        ["nesting of composite glyphs"] = ("`Fonts/Tables/GlyphOutlineDecoder.cs`", "`Documents.Font.Parse.InvalidValue`"),
        ["clips open one inside the other on a page, for both back-ends"] = ("`PageModel/PageModelRules.cs`", "`Documents.Pdf.Write.InvalidPageModel`, `Documents.Raster.InvalidPageModel`"),
        ["slices of one piece of content taller than the page area (`MonolithicOverflow.Slice`)"] = ("`Layout/MonolithicSlices.cs`", "`Documents.Layout.TooManySlices`"),
        ["slices of one document, all pieces together (`MonolithicOverflow.Slice`)"] = ("`Layout/MonolithicSlices.cs`, `Layout/Paginator.cs`", "`Documents.Layout.TooManySlices` (the message names the limit for one document)"),
    };

    /// <summary>The types whose public constants are limits: each such constant has a row.</summary>
    private static readonly (string Assembly, string Type)[] LimitTypes =
    [
        ("Ave.Extensions.Documents.Css", "Ave.Extensions.Documents.Css.CssInputLimits"),
        ("Ave.Extensions.Documents.Html.Parsing", "Ave.Extensions.Documents.Html.Parsing.HtmlInputLimits"),
        ("Ave.Extensions.Documents.Images", "Ave.Extensions.Documents.Images.ImageLimits"),
        ("Ave.Extensions.Documents.Fluent", "Ave.Extensions.Documents.Fluent.InputLimits"),
    ];

    /// <summary>The rows of the table: limit and value cell.</summary>
    private static List<(string Limit, string Value, string Where, string Error)> Table()
    {
        var lines = File.ReadAllLines(Path.Combine(ProjectRulesTests.FamilyRoot(), "docs", "architecture.md"));
        var start = Array.FindIndex(lines, line => line.StartsWith("## Bounded work", StringComparison.Ordinal));
        start.Should().BeGreaterThan(0);
        var header = Array.FindIndex(lines, start, line => line.StartsWith("| Limit | Value |", StringComparison.Ordinal));
        return [.. lines.Skip(header + 2).TakeWhile(line => line.StartsWith('|')).Select(line => line.Trim().Trim('|').Split(" | "))
            .Select(cells => (cells[0].Trim(), cells[1].Trim(), cells[2].Trim(), cells[3].Trim()))];
    }

    [Fact]
    public void Every_row_of_the_table_is_checked_and_says_what_is_checked()
    {
        var table = Table();

        table.Select(row => row.Limit).Should().BeEquivalentTo(Rows.Keys);
        table.Select(row => row.Limit).Should().BeEquivalentTo(Places.Keys);
        foreach (var (limit, value, where, error) in table)
        {
            value.Should().Be(Rows[limit].Value, limit);
            (where, error).Should().Be(Places[limit], limit);
        }
    }

    [Fact]
    public void Every_public_limit_constant_has_a_row()
    {
        var checkedConstants = Rows.Values.SelectMany(row => row.Constants).Select(constant => (constant.Type, constant.Member)).ToHashSet();
        foreach (var (assembly, typeName) in LimitTypes)
        {
            var type = Assembly.Load(new AssemblyName(assembly)).GetType(typeName, throwOnError: true)!;
            foreach (var member in type.GetFields(BindingFlags.Static | BindingFlags.Public).Where(field => field.IsLiteral || field.IsInitOnly)
                .Select(field => field.Name).Concat(type.GetProperties(BindingFlags.Static | BindingFlags.Public).Select(property => property.Name)))
            {
                checkedConstants.Should().Contain((typeName, member), $"{typeName}.{member} is a public limit, so it has a row");
            }
        }
    }

    [Fact]
    public void Every_constant_behind_a_row_has_the_value_the_row_states()
    {
        foreach (var (limit, (_, constants)) in Rows)
        {
            foreach (var constant in constants)
            {
                Read(constant).Should().Be(constant.Value, $"{limit}: {constant.Type}.{constant.Member}");
            }
        }
    }

    /// <summary>A constant, static field or static property (a length in points), public or not, as a number.</summary>
    private static double Read(Constant constant)
    {
        var type = Assembly.Load(new AssemblyName(constant.Assembly)).GetType(constant.Type, throwOnError: true)!;
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var value = type.GetField(constant.Member, Static) is { } field
            ? (field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(null))
            : type.GetProperty(constant.Member, Static)!.GetValue(null);
        return value switch
        {
            int number => number,
            long number => number,
            double number => number,
            _ => (double)value!.GetType().GetProperty("Points")!.GetValue(value)!,
        };
    }
}

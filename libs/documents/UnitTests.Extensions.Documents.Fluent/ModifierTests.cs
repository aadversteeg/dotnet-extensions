using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.TestSupport;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// The modifier rules of the code front-end: each modifier returns a new value with one
/// property set, modifiers are order-free, precedence is fixed (own over style, side over all sides), and the
/// element keeps the file and line of its factory call.
/// </summary>
public class ModifierTests
{
    private static readonly Color Navy = new(0x1F, 0x4E, 0x79);
    private static readonly Color Red = new(0xFF, 0, 0);

    private static readonly Func<ParagraphElement, ParagraphElement>[] Modifiers =
    [
        p => p.MarginBottom(12.Pt()),
        p => p.Margin(3.Pt()),
        p => p.PaddingTop(2.Pt()),
        p => p.Padding(5.Pt()),
        p => p.Background(Color.White),
        p => p.Color(Navy),
        p => p.FontSize(9.Pt()),
        p => p.Bold(),
        p => p.With(Style.Empty.Color(Red).MarginTop(7.Pt())),
    ];

    private static Box Translated(ParagraphElement paragraph) =>
        (Box)DocumentTranslator.ToModel(Document([paragraph]).FontFamily("Lato")).Value.Root.Children[0];

    [Fact]
    public void Modifiers_give_the_same_element_in_every_order()
    {
        var start = Paragraph("x");
        var expected = Modifiers.Aggregate(start, (element, modify) => modify(element));

        // Every rotation, and the reverse of every rotation, of the nine modifiers above.
        var orders = Enumerable.Range(0, Modifiers.Length)
            .SelectMany(shift => new[] { Modifiers.Skip(shift).Concat(Modifiers.Take(shift)).ToArray(), Modifiers.Skip(shift).Concat(Modifiers.Take(shift)).Reverse().ToArray() });

        foreach (var order in orders)
        {
            order.Aggregate(start, (element, modify) => modify(element)).Should().Be(expected);
        }
    }

    [Fact]
    public void A_single_side_wins_over_the_all_sides_value_in_either_order()
    {
        var paragraph = Paragraph("x");
        var first = paragraph.Padding(4.Pt()).PaddingTop(0.Pt());
        var second = paragraph.PaddingTop(0.Pt()).Padding(4.Pt());

        first.Should().Be(second);
        Translated(first).Style.Padding.Should().Be(new Edges(0.Pt(), 4.Pt(), 4.Pt(), 4.Pt()));
    }

    [Fact]
    public void Own_values_win_over_the_shared_style_in_either_order()
    {
        var style = Style.Empty.Color(Red).MarginBottom(8.Pt()).Margin(1.Pt());
        var paragraph = Paragraph("x");
        var first = paragraph.With(style).Color(Navy).Margin(2.Pt());
        var second = paragraph.Margin(2.Pt()).Color(Navy).With(style);

        first.Should().Be(second);
        var box = Translated(first);
        box.Style.Margin.Should().Be(Edges.All(2.Pt()), "the own all-sides margin wins over every margin of the style");
        ((TextRun)box.Children[0]).Style.Color.Should().Be(Navy);
    }

    [Fact]
    public void The_style_supplies_what_the_element_does_not_set()
    {
        var box = Translated(Paragraph("x").With(Style.Empty.Color(Red).MarginBottom(8.Pt())).MarginTop(1.Pt()));

        box.Style.Margin.Should().Be(new Edges(1.Pt(), 0.Pt(), 8.Pt(), 0.Pt()));
        ((TextRun)box.Children[0]).Style.Color.Should().Be(Red);
    }

    [Fact]
    public void Setting_the_same_property_twice_keeps_the_last_value() =>
        Paragraph("x").Color(Red).Color(Navy).Should().Be(Paragraph("x").Color(Navy));

    [Fact]
    public void A_modifier_returns_a_new_value_and_leaves_the_original_unchanged()
    {
        var original = Heading(1, "Title");

        var changed = original.MarginBottom(12.Pt()).Color(Navy);

        changed.Should().NotBe(original);
        original.Format.Should().Be(Style.Empty);
        changed.Should().BeOfType<ParagraphElement>("a modifier keeps the type it was called on");
    }

    [Fact]
    public void A_style_is_built_with_the_same_modifiers()
    {
        var style = Style.Empty.FontSize(9.Pt()).Padding(4.Pt()).Background(Navy);

        style.Text.Should().Be(new TextFormat(FontSize: 9.Pt()));
        style.Box.Should().Be(new BoxFormat(Padding: 4.Pt(), Background: Navy));
    }

    [Fact]
    public void An_element_keeps_the_location_of_its_factory_call_not_of_its_modifiers()
    {
        var line = CurrentLine() + 1;
        var heading = Heading(1, "Title")
            .MarginBottom(12.Pt())
            .Color(Navy);

        heading.Source.Line.Should().Be(line);
    }

    [Fact]
    public void Bold_text_is_text_with_the_bold_modifier() =>
        Bold("x", "file.cs", 3).Should().Be(Text("x", "file.cs", 3).Bold());

    [Fact]
    public void Text_align_is_inherited_from_a_block_by_its_paragraphs_and_can_be_overridden()
    {
        var model = DocumentTranslator.ToModel(Document(
            [Block([Paragraph("a"), Paragraph("b").TextAlign(TextAlign.Right)]).TextAlign(TextAlign.Justify)]).FontFamily("Lato")).Value;

        var block = (Box)model.Root.Children[0];
        block.Style.TextAlign.Should().Be(TextAlign.Justify);
        block.Children.Cast<Box>().Select(paragraph => paragraph.Style.TextAlign).Should().Equal(TextAlign.Justify, TextAlign.Right);
        model.Root.Style.TextAlign.Should().Be(TextAlign.Left, "the initial value");
    }

    private static int CurrentLine([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line;
    [Fact]
    public void Orphans_and_widows_set_on_the_document_reach_every_paragraph()
    {
        // Review 40: .Orphans() and .Widows() did not compile on Document, so getting the pages of 0.1.0-preview.2 back
        // (no orphans or widows rule) needed a Block around everything.
        var document = Document([Paragraph("a"), Block([Paragraph("b")]).Orphans(3)], Page(200.Pt(), 200.Pt()), title: "T", language: "en")
            .FontFamily("Lato").Orphans(1).Widows(1);

        var model = DocumentTranslator.ToModel(document).Value;
        var first = (Box)model.Root.Children[0];
        var inner = (Box)((Box)model.Root.Children[1]).Children[0];
        (first.Style.Orphans, first.Style.Widows).Should().Be((1, 1));
        (inner.Style.Orphans, inner.Style.Widows).Should().Be((3, 1), "a box's own value wins over the document's");
    }

    [Fact]
    public void The_text_format_record_keeps_its_positional_shape_and_holds_the_new_values_in_its_body()
    {
        // Binary compatibility with 0.1.0-preview.2: six positional values; orphans, widows and line height are body
        // properties.
        var format = new TextFormat("Lato", 9.Pt(), null, null, null, null) { Orphans = 3, Widows = 4, LineHeight = new LineHeightValue.Number(1.5) };

        typeof(TextFormat).GetConstructors().Should().ContainSingle(constructor => constructor.GetParameters().Length == 6);
        (format.Orphans, format.Widows, format.LineHeight).Should().Be((3, 4, new LineHeightValue.Number(1.5)));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1.0)]
    public void An_image_s_text_values_are_checked_as_any_element_s(double lineHeight)
    {
        // Review 40 (inline sub-review, finding 7): an image took a line height and every other text value unchecked.
        var document = Document([Image(TestImages.Bytes("rgb8.png")).Decorative().LineHeight(lineHeight)], Page(200.Pt(), 200.Pt()), title: "T", language: "en").FontFamily("Lato");

        DocumentTranslator.ToModel(document).Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.LineHeight);
    }
}

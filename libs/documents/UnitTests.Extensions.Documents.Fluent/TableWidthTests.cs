using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// A table is as wide as its columns need by default (CSS <c>width: auto</c>); <c>FullWidth()</c> makes it as wide as
/// the space it is in (<see cref="BoxWidth.Stretch"/>, an HTML table with <c>width: 100%</c>).
/// </summary>
public class TableWidthTests
{
    private static Box ModelOf(Element element) =>
        DocumentTranslator.ToModel(Document([element]).FontFamily("Lato")).Value.Root.Children.OfType<Box>().Single();

    private static TableElement Grid() => Table(Columns(Auto, Auto), [TableRow([Cell("a"), Cell("b")])]);

    [Fact]
    public void A_table_is_as_wide_as_its_columns_need_by_default() =>
        ModelOf(Grid()).Style.Width.Should().Be(BoxWidth.Auto);

    [Fact]
    public void FullWidth_chooses_stretch() =>
        ModelOf(Grid().FullWidth()).Style.Width.Should().Be(BoxWidth.Stretch);

    [Fact]
    public void The_model_text_names_a_stretched_width()
    {
        var document = Document([Grid().FullWidth()]).FontFamily("Lato");

        var model = DocumentTranslator.ToModel(document).Value;

        DocumentModelText.Write(model).Should().Contain("width=stretch");
    }

    [Fact]
    public void Preferred_makes_a_column_of_the_automatic_layout_with_a_given_width()
    {
        var box = ModelOf(Table(Columns(Preferred(2.Cm()), Auto), [TableRow([Cell("a"), Cell("b")])]));

        box.Style.Columns.Should().Equal(new TrackSize.Preferred(2.Cm()), TrackSize.Auto.Instance);
        DocumentModelText.Write(DocumentTranslator.ToModel(Document([Table(Columns(Preferred(2.Cm()), Auto), [TableRow([Cell("a"), Cell("b")])])]).FontFamily("Lato")).Value)
            .Should().Contain("columns=preferred(");
    }

    [Fact]
    public void A_negative_preferred_width_is_refused() =>
        DocumentTranslator.ToModel(Document([Table(Columns(Preferred((-1).Pt())), [TableRow([Cell("a")])])]).FontFamily("Lato"))
            .Error.Parts().Select(part => part.Code).Should().Contain(DocumentErrorCodes.Input.Invalid.Columns);
}

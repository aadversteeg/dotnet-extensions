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
}

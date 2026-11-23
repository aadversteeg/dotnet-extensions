using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// The checks on lists: the code front-end's on what an author writes (<c>InputChecks.List</c>) and the model check's on a
/// list structure built by hand (<see cref="ModelCheck"/>), each <c>Documents.Input.Invalid.List</c> at the source.
/// </summary>
public class ListInputTests
{
    private static readonly SourceLocation Here = new("ListInputTests.cs", 1);

    private static Error ErrorOf(Document document)
    {
        var result = DocumentTranslator.ToModel(document.FontFamily("Lato"));
        result.IsFailure.Should().BeTrue();
        return result.Error;
    }

    public static TheoryData<string> AuthorErrors() => ["empty", "start on bullets", "bullet on numbers", "past int", "too deep", "bullet value"];

    [Theory]
    [MemberData(nameof(AuthorErrors))]
    public void A_list_an_author_cannot_mean_is_refused_at_its_line(string which)
    {
        ListElement Deep(int levels) => levels == 1 ? BulletedList([ListItem("x")]) : BulletedList([ListItem([Deep(levels - 1)])]);
        var list = which switch
        {
            "empty" => BulletedList([]),
            "start on bullets" => BulletedList([ListItem("x")]).Start(3),
            "bullet on numbers" => NumberedList([ListItem("x")]).Bullet(ListBullet.Square),
            "past int" => NumberedList([ListItem("x"), ListItem("y")]).Start(int.MaxValue),
            "too deep" => Deep(ModelLimits.MaxListDepth + 1),
            _ => BulletedList([ListItem("x")]).Bullet((ListBullet)9),
        };
        var error = ErrorOf(Document([list]));

        error.Parts().Should().Contain(part => part.Code == DocumentErrorCodes.Input.Invalid.List);
        error.ToString().Should().Contain(nameof(ListInputTests));
    }

    [Fact]
    public void The_last_number_of_the_32_bit_range_is_allowed()
    {
        var model = DocumentTranslator.ToModel(Document([NumberedList([ListItem("x")]).Start(int.MaxValue)]).FontFamily("Lato"));
        model.IsSuccess.Should().BeTrue(model.IsFailure ? model.Error.ToString() : string.Empty);
    }

    private static TextStyle Text => new("Lato", Length.FromPoints(12), FontWeight.Regular, Color.Black, FontStyle.Normal);

    private static Box Body(int id) => new(new NodeId(id), SemanticRole.ListBody, LayoutAlgorithmId.Flow, BoxStyle.Initial, [], Here);

    private static Box Label(int id, params Node[] children) =>
        new(new NodeId(id), SemanticRole.ListLabel(null), LayoutAlgorithmId.Inline, BoxStyle.Initial, [.. children], Here);

    private static Box Item(int id, params Node[] children) =>
        new(new NodeId(id), SemanticRole.ListItem, LayoutAlgorithmId.ListItem, BoxStyle.Initial, [.. children], Here);

    private static Box List(params Node[] children) =>
        new(new NodeId(1), SemanticRole.List(ListNumbering.Decimal), LayoutAlgorithmId.Flow, BoxStyle.Initial, [.. children], Here);

    private static Result<StyledDocument, Error> Check(params Node[] body) =>
        ModelCheck.Check(new StyledDocument(new DocumentMetadata(null, null), new PageStyle(Length.FromPoints(300), Length.FromPoints(300), Edges.Zero),
            new Box(new NodeId(0), SemanticRole.Document, LayoutAlgorithmId.Flow, BoxStyle.Initial, [.. body], Here)));

    public static TheoryData<string> ModelErrors() =>
        ["item outside a list", "other child in a list", "item without body", "body first", "label outside an item", "symbol outside a label",
            "label too long", "label of a counter", "item not laid out as one", "list not flow", "numbering value"];

    [Theory]
    [MemberData(nameof(ModelErrors))]
    public void A_list_structure_layout_cannot_lay_out_is_refused(string which)
    {
        var run = new TextRun(new NodeId(9), "1. ", Text, Here);
        Node node = which switch
        {
            "item outside a list" => Item(2, Body(3)),
            "other child in a list" => List(Body(2)),
            "item without body" => List(Item(2, Label(3, run))),
            "body first" => List(Item(2, Body(3), Label(4, run))),
            "label outside an item" => Label(2, run),
            "symbol outside a label" => new Box(new NodeId(2), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial,
                [new MarkerSymbol(new NodeId(3), MarkerShape.Disc, Text, Here)], Here),
            "label too long" => List(Item(2, Label(3, new TextRun(new NodeId(9), new string('1', ModelLimits.MaxLabelLength + 1), Text, Here)), Body(4))),
            "label of a counter" => List(Item(2, Label(3, new CounterRun(new NodeId(5), PageCounter.PageNumber, Text, Here)), Body(4))),
            "item not laid out as one" => List(Item(2, Body(3)) with { Layout = LayoutAlgorithmId.Flow }),
            "list not flow" => List(Item(2, Body(3))) with { Layout = new LayoutAlgorithmId("other") },
            _ => new Box(new NodeId(1), SemanticRole.List((ListNumbering)42), LayoutAlgorithmId.Flow, BoxStyle.Initial, [Item(2, Body(3))], Here),
        };
        var result = Check(node);

        result.IsFailure.Should().BeTrue(which);
        result.Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.List, result.Error.ToString());
    }

    [Fact]
    public void A_list_of_items_with_a_label_of_text_or_a_symbol_or_none_passes()
    {
        var label = Label(3, new TextRun(new NodeId(9), new string('1', ModelLimits.MaxLabelLength), Text, Here));
        var symbol = Label(6, new MarkerSymbol(new NodeId(7), MarkerShape.Square, Text, Here));
        Check(List(Item(2, label, Body(4)), Item(5, symbol, Body(8)), Item(10, Body(11)))).IsSuccess.Should().BeTrue();
    }
}

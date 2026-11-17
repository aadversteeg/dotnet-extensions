using Ave.Documents.Pdf;

namespace UnitTests.Ave.Documents.Pdf;

/// <summary>
/// One rule for bad input in the PDF library: the writer checks the objects it is given and answers with an error
/// value — a number that is not finite, two entries with one key, a stream dictionary that sets what the writer
/// sets. Objects are values: two equal arrays, strings or dictionaries are equal.
/// </summary>
public class PdfObjectRulesTests
{
    private static PdfFile FileWith(PdfObject catalogEntry, PdfStream? stream = null) => new(
        [
            new IndirectValue(1, PdfDictionary.Of(("Type", new PdfName("Catalog")), ("Pages", new PdfReference(2)), ("Extra", catalogEntry))),
            new IndirectValue(2, PdfDictionary.Of(("Type", new PdfName("Pages")), ("Kids", new PdfArray([])), ("Count", new PdfInteger(0)))),
            .. stream is null ? [] : new IndirectObject[] { new IndirectStream(3, stream) },
        ],
        Catalog: new PdfReference(1),
        Info: null,
        FileIdentifier: null);

    [Fact]
    public void Two_entries_with_one_key_are_an_error_value()
    {
        var duplicate = new PdfDictionary([KeyValuePair.Create(new PdfName("A"), (PdfObject)new PdfInteger(1)), KeyValuePair.Create(new PdfName("A"), (PdfObject)new PdfInteger(2))]);

        var result = PdfWriter.Write(FileWith(duplicate));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(PdfErrorCodes.Write.InvalidObject);
    }

    [Theory]
    [InlineData("Length")]
    [InlineData("Filter")]
    public void A_stream_dictionary_that_sets_what_the_writer_sets_is_an_error_value(string key)
    {
        var stream = new PdfStream(PdfDictionary.Of((key, new PdfInteger(3))), [.. "abc"u8], StreamEncoding.None);

        var result = PdfWriter.Write(FileWith(new PdfInteger(0), stream));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(PdfErrorCodes.Write.InvalidObject);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(3.5e38)]
    [InlineData(-1e300)]
    public void A_number_that_is_not_finite_or_beyond_the_real_range_is_an_error_value_wherever_it_is(double value)
    {
        PdfWriter.Write(FileWith(new PdfArray([new PdfArray([new PdfReal(value)])]))).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidNumber);
        PdfSerializer.Serialize(new PdfReal(value)).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidNumber);
        ContentStream.Serialize([ContentOperators.Rectangle(0, 0, value, 1)]).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidNumber);
    }

    [Theory]
    [InlineData(2147483647.0, "2147483647")]
    [InlineData(-2147483648.0, "-2147483648")]
    [InlineData(2147483648.0, "2147483648.0")]
    [InlineData(-2147483649.0, "-2147483649.0")]
    [InlineData(3e9, "3000000000.0")]
    [InlineData(1e20, "100000000000000000000.0")]
    public void A_whole_number_outside_the_integer_range_is_written_as_a_real_with_a_decimal_point(double value, string expected)
    {
        // ISO 32000-1 Annex C, Table C.1: an integer is at most 2^31 - 1 and at least -2^31 (veraPDF rule 6.1.13-1).
        // A reader tells a real from an integer by its decimal point (§7.3.3), so the real must carry one.
        PdfSerializer.Serialize(PdfNumber.From(value)).Value.Should().Be(expected);
        ContentStream.Serialize([ContentOperators.Rectangle(0, 0, value, 1)]).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(5.0, "5.0")]
    [InlineData(0.0, "0.0")]
    [InlineData(0.00001, "0.0")]
    [InlineData(-7.00004, "-7.0")]
    [InlineData(1e20, "100000000000000000000.0")]
    public void A_real_is_never_written_in_the_form_of_an_integer(double value, string expected) =>
        PdfSerializer.Serialize(new PdfReal(value)).Value.Should().Be(expected);

    [Theory]
    [InlineData(-12.0, -12)]
    [InlineData(3.00004, 3)]
    public void A_number_that_is_whole_at_four_decimals_and_in_the_integer_range_is_an_integer(double value, int expected) =>
        PdfNumber.From(value).Should().Be(new PdfInteger(expected));

    [Fact]
    public void The_largest_real_is_written()
    {
        PdfSerializer.Serialize(new PdfReal(PdfSerializer.MaxReal)).IsSuccess.Should().BeTrue();
        PdfSerializer.Serialize(new PdfReal(-PdfSerializer.MaxReal)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Arrays_strings_and_dictionaries_with_equal_contents_are_equal()
    {
        new PdfArray([new PdfInteger(1), new PdfName("A")]).Should().Be(new PdfArray([new PdfInteger(1), new PdfName("A")]));
        PdfString.FromText("Title").Should().Be(PdfString.FromText("Title"));
        PdfDictionary.Of(("A", new PdfInteger(1))).Should().Be(PdfDictionary.Of(("A", new PdfInteger(1))));
        PdfDictionary.Of(("A", new PdfInteger(1))).GetHashCode().Should().Be(PdfDictionary.Of(("A", new PdfInteger(1))).GetHashCode());
    }

    public static TheoryData<string, PdfObject> MissingParts => new()
    {
        { "an array item", new PdfArray([null!]) },
        { "a name's text", new PdfName(null!) },
        { "a dictionary key", new PdfDictionary([KeyValuePair.Create<PdfName, PdfObject>(null!, new PdfInteger(1))]) },
        { "a dictionary value", new PdfDictionary([KeyValuePair.Create<PdfName, PdfObject>("A", null!)]) },
        { "a string form", new PdfString([], (PdfStringForm)7) },
        { "an object number", new PdfReference(0) },
    };

    [Theory]
    [MemberData(nameof(MissingParts))]
    public void A_missing_or_undefined_part_of_an_object_is_an_error_value_wherever_it_is(string what, PdfObject value)
    {
        PdfSerializer.Serialize(value).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidObject, what);
        PdfWriter.Write(FileWith(new PdfArray([value]))).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidObject, what);
    }

    /// <summary>An array holding an array, <paramref name="depth"/> levels: the outermost array is level 1.</summary>
    private static PdfObject Nested(int depth) =>
        Enumerable.Range(1, depth - 1).Aggregate((PdfObject)new PdfArray([new PdfInteger(1)]), (inner, _) => new PdfArray([inner]));

    [Theory]
    [InlineData(3_000)]
    [InlineData(100_000)]
    public void An_object_nested_deeper_than_the_limit_is_an_error_value_not_a_stack_overflow(int depth)
    {
        PdfSerializer.Serialize(Nested(depth)).Error.Code.Should().Be(PdfErrorCodes.Write.NestedTooDeep);
        PdfWriter.Write(FileWith(Nested(depth))).Error.Code.Should().Be(PdfErrorCodes.Write.NestedTooDeep);
    }

    [Fact]
    public void The_nesting_limit_counts_arrays_and_dictionaries_from_the_outermost_object()
    {
        // The limit holds for the object given: a dictionary counts as a level as an array does.
        PdfSerializer.Serialize(Nested(PdfSerializer.MaxNestingDepth)).IsSuccess.Should().BeTrue();
        PdfSerializer.Serialize(Nested(PdfSerializer.MaxNestingDepth + 1)).Error.Code.Should().Be(PdfErrorCodes.Write.NestedTooDeep);
        PdfSerializer.Serialize(PdfDictionary.Of(("A", Nested(PdfSerializer.MaxNestingDepth - 1)))).IsSuccess.Should().BeTrue();
        PdfSerializer.Serialize(PdfDictionary.Of(("A", Nested(PdfSerializer.MaxNestingDepth)))).Error.Code.Should().Be(PdfErrorCodes.Write.NestedTooDeep);
    }

    [Fact]
    public void Every_object_the_writer_writes_is_counted_from_its_own_top()
    {
        // Each indirect object is a tree of its own: the catalog entry here is one level below the catalog dictionary.
        PdfWriter.Write(FileWith(Nested(PdfSerializer.MaxNestingDepth - 1))).IsSuccess.Should().BeTrue();
        PdfWriter.Write(FileWith(Nested(PdfSerializer.MaxNestingDepth))).Error.Code.Should().Be(PdfErrorCodes.Write.NestedTooDeep);
    }

    [Fact]
    public void An_unset_array_is_read_as_empty_as_null_is()
    {
        PdfString.FromBytes(default(System.Collections.Immutable.ImmutableArray<byte>)).Bytes.Should().BeEmpty();
        PdfArray.OfNumbers(default(System.Collections.Immutable.ImmutableArray<double>)).Items.Should().BeEmpty();
        PdfDictionary.Of(default(System.Collections.Immutable.ImmutableArray<(PdfName, PdfObject?)>)).Entries.Should().BeEmpty();
        ContentStream.Serialize(default(System.Collections.Immutable.ImmutableArray<ContentOperation?>)).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidObject);
    }

    [Theory]
    [InlineData(-7)]
    [InlineData(0)]
    [InlineData(99)]
    public void An_info_reference_to_no_object_of_the_file_is_an_error_value(int number) =>
        PdfWriter.Write(FileWith(new PdfInteger(0)) with { Info = new PdfReference(number) }).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidObject);

    [Fact]
    public void An_info_reference_to_an_object_of_the_file_is_written()
    {
        var file = FileWith(new PdfInteger(0)) with { Info = new PdfReference(2) };

        System.Text.Encoding.ASCII.GetString([.. PdfWriter.Write(file).Value]).Should().Contain("/Info 2 0 R");
    }

    /// <summary>A case of PdfObject from another assembly: possible, since a record's copy constructor is protected.</summary>
    private sealed record ForeignObject(PdfObject Original) : PdfObject(Original);

    [Fact]
    public void An_object_of_a_type_from_another_assembly_is_an_error_value()
    {
        PdfSerializer.Serialize(new ForeignObject(PdfNull.Instance)).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidObject);
        PdfWriter.Write(FileWith(new PdfArray([new ForeignObject(PdfNull.Instance)]))).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidObject);
    }

    [Fact]
    public void A_stream_object_without_its_stream_is_an_error_value()
    {
        var file = FileWith(new PdfInteger(0));

        PdfWriter.Write(file with { Objects = [.. file.Objects, new IndirectStream(3, null!)] }).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidObject);
    }

    [Fact]
    public void A_missing_file_object_list_or_catalog_is_an_error_value()
    {
        var file = FileWith(new PdfInteger(0));

        PdfWriter.Write(null).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidObject);
        PdfWriter.Write(file with { Objects = default }).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidObject);
        PdfWriter.Write(file with { Catalog = null! }).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidObject);
        PdfWriter.Write(file with { Objects = [.. file.Objects, null!] }).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidObject);
        ContentStream.Serialize(null).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidObject);
        ContentStream.Serialize([null]).Error.Code.Should().Be(PdfErrorCodes.Write.InvalidObject);
    }
}

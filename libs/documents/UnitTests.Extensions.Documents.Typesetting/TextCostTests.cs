using System.Text;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting;
using Ave.Extensions.Documents.Typesetting.Shaping;
using Ave.Extensions.Documents.Typesetting.Unicode;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Typesetting;

/// <summary>
/// Bounded work on text: line breaking and shaping lines again are linear in the paragraph, putting marks on a base and
/// normalizing are linear in the cluster. Each input that showed the quadratic cases is run at two sizes, the
/// second twice the first, and its work is COUNTED, not timed: characters handed to the reshaper, operations charged to the
/// shaping budget, the normalizer's steps, and the bytes allocated on the thread. Linear work at most doubles (with a margin
/// for the fixed part); a quadratic term quadruples.
/// </summary>
public class TextCostTests(ITestOutputHelper output)
{
    /// <summary>Twice the input may cost at most this many times the work: linear with room for a fixed part, far below
    /// the four times of quadratic work.</summary>
    private const double Linear = 2.5;

    private sealed record Work(long Characters, long Calls, long Operations, long Bytes);

    /// <summary>The paragraph shaped once, then broken with the layout's wiring: every piece shaped again under the
    /// paragraph's budget. Counted: what the breaker hands the reshaper, the operations all pieces charged, the bytes the
    /// breaking allocated.</summary>
    private static Work Break(string text, double fontSize, double width, RunWhiteSpace? mode = null)
    {
        var font = TestFonts.Regular;
        var glyphs = Shaper.Shape(text, font, fontSize).Value;
        var (characters, calls) = (0L, 0L);
        LineShapingBudget? seen = null;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var lines = GreedyLineBreaker.BreakLines([[.. glyphs]], [mode ?? RunWhiteSpace.Initial], width, (_, piece, showsHyphen, budget) =>
        {
            seen = budget;
            characters += piece.Length;
            calls++;
            return Shaper.ShapeLinePiece(piece, font, fontSize, showsHyphen, budget);
        }, LineEndShaping.KeepParagraphGlyphs);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        lines.IsSuccess.Should().BeTrue(lines.IsFailure ? lines.Error.ToString() : string.Empty);
        return new Work(characters, calls, seen?.Shaping.Spent ?? 0, bytes);
    }

    private static string Words(int count) => string.Join(" ", Enumerable.Range(0, count).Select(index => "word" + index % 10));

    private static string SoftHyphens(int count) => string.Join("\u00AD", Enumerable.Range(0, count).Select(index => (char)('a' + index % 26)));

    private static string KernedWords(int count) => string.Join(" ", Enumerable.Repeat("AVAVAV", count));

    private void Linearly(string what, int size, Work small, Work large)
    {
        output.WriteLine($"{what}: {size} -> {small}; {2 * size} -> {large}");
        large.Characters.Should().BeLessThanOrEqualTo((long)(Linear * small.Characters) + 64, $"{what}: characters shaped again");
        large.Calls.Should().BeLessThanOrEqualTo((long)(Linear * small.Calls) + 16, $"{what}: pieces shaped again");
        large.Operations.Should().BeLessThanOrEqualTo((long)(Linear * small.Operations) + 1024, $"{what}: shaping operations of the pieces");
        large.Bytes.Should().BeLessThanOrEqualTo((long)(Linear * small.Bytes), $"{what}: bytes allocated");
    }

    [Fact]
    public void A_paragraph_of_many_words_on_one_tall_page_is_broken_in_linear_work()
    {
        // The input `para 8000 h=14000 w=300 fs=2`: hundreds of lines on one page, each candidate measured.
        Break(Words(500), 2, 276);
        Linearly("words", 4000, Break(Words(4000), 2, 276), Break(Words(8000), 2, 276));
    }

    [Fact]
    public void Preserved_spaces_that_hang_at_every_line_end_are_broken_in_linear_work()
    {
        // Words with runs of preserved spaces between them, in a narrow paragraph: every line ends with spaces that hang.
        static string Spaced(int count) => string.Join("        ", Enumerable.Range(0, count).Select(index => "word" + index % 10));
        var preserved = new RunWhiteSpace(Ave.Extensions.Documents.Core.WhiteSpaceCollapse.Preserve);
        Break(Spaced(100), 2, 20, preserved);
        Linearly("hanging spaces", 2000, Break(Spaced(2000), 2, 20, preserved), Break(Spaced(4000), 2, 20, preserved));
    }

    [Fact]
    public void A_nowrap_line_of_many_words_is_broken_in_linear_work()
    {
        // One line of thousands of words that may not wrap, far wider than its paragraph.
        var nowrap = new RunWhiteSpace(Ave.Extensions.Documents.Core.WhiteSpaceCollapse.Collapse, Ave.Extensions.Documents.Core.TextWrapMode.NoWrap);
        Break(Words(100), 2, 50, nowrap);
        Linearly("nowrap", 2000, Break(Words(2000), 2, 50, nowrap), Break(Words(4000), 2, 50, nowrap));
    }

    [Fact]
    public void A_line_with_a_soft_hyphen_between_every_two_letters_is_broken_in_linear_work()
    {
        // `para 2000 kind=shy h=14000 w=14000 fs=1`: one line of thousands of break opportunities.
        Break(SoftHyphens(100), 1, 13976);
        Linearly("soft hyphens", 2000, Break(SoftHyphens(2000), 1, 13976), Break(SoftHyphens(4000), 1, 13976));
    }

    [Fact]
    public void Kerned_words_that_join_every_glyph_to_the_one_before_are_broken_in_linear_work()
    {
        // `para 4000 kind=kernwords h=14000 w=14000 fs=1`: no safe place in the whole paragraph, so an end shaped again
        // reaches back to the line's start or on to its end. On the wide page (about 3 000 words a line) the work is at
        // most a few times the text; in lines of about 200 words it grows linearly with the paragraph.
        Break(KernedWords(100), 1, 13976);
        var wide = Break(KernedWords(4000), 1, 13976);
        output.WriteLine($"kerned words on the wide page: {wide}");
        wide.Characters.Should().BeLessThanOrEqualTo(2 * 7 * 4000, "each line's text is shaped again at most a few times");
        var small = Break(KernedWords(4000), 1, 1000);
        var large = Break(KernedWords(8000), 1, 1000);
        Linearly("kerned words", 4000, small, large);
        small.Characters.Should().BePositive("the lines of the smaller paragraph are shaped again too, so the comparison is not vacuous");
    }

    [Fact]
    public void All_pieces_of_a_paragraph_cut_into_many_lines_share_one_budget_and_stay_under_it()
    {
        // The soft-hyphen input in a narrow column (`para n kind=shy w=30`), with letters that kern across the soft
        // hyphens, so that every line end is shaped again: thousands of pieces, one paragraph.
        var text = string.Join("\u00AD", Enumerable.Range(0, 4000).Select(index => index % 2 == 0 ? "A" : "V"));
        var font = TestFonts.Regular;
        var glyphs = Shaper.Shape(text, font, 12).Value;
        var budgets = new HashSet<LineShapingBudget>(ReferenceEqualityComparer.Instance);
        var (characters, calls) = (0L, 0L);
        var lines = GreedyLineBreaker.BreakLines([[.. glyphs]], 6, (_, piece, showsHyphen, budget) =>
        {
            budgets.Add(budget);
            (characters, calls) = (characters + piece.Length, calls + 1);
            return Shaper.ShapeLinePiece(piece, font, 12, showsHyphen, budget);
        });

        lines.IsSuccess.Should().BeTrue(lines.IsFailure ? lines.Error.ToString() : string.Empty);
        output.WriteLine($"{lines.Value.Count} lines, {calls} pieces, {characters} characters shaped again");
        calls.Should().BeGreaterThan(1000, "the paragraph is cut into many pieces");
        var shared = budgets.Should().ContainSingle("every piece of the paragraph gets the paragraph's budget").Subject;
        shared.Length.Should().Be(text.Length);
        shared.Shaping.Spent.Should().BePositive().And.BeLessThanOrEqualTo(shared.Shaping.OperationLimit);
        characters.Should().BeLessThanOrEqualTo(shared.CharacterLimit);
        shared.Shaping.OperationLimit.Should().Be(ShapingBudget.MaxOperations(text.Length), "the pieces together may spend what shaping the paragraph once may");
    }

    /// <summary>One shaping of the text under a budget the test reads: operations charged, bytes allocated.</summary>
    private static Work Shape(string text)
    {
        var budget = new ShapingBudget(text.Length);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var shaped = Shaper.Shape(text, TestFonts.Regular, ShapingSize.Exact(12), budget);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        shaped.IsSuccess.Should().BeTrue(shaped.IsFailure ? shaped.Error.ToString() : string.Empty);
        return new Work(text.Length, 1, budget.Spent, bytes);
    }

    [Fact]
    public void Marks_on_one_base_are_positioned_in_work_linear_in_their_number()
    {
        // `Shaper.Shape("a" + n x U+0301)`: every mark attaches to the same base, and each attachment marks the glyphs
        // from the base to the mark unsafe to break.
        static string Marks(int count) => "a" + string.Concat(Enumerable.Repeat("\u0301", count));
        Shape(Marks(100));
        Linearly("marks", 4000, Shape(Marks(4000)), Shape(Marks(8000)));
    }

    [Fact]
    public void Dots_below_and_one_mark_above_that_composes_across_them_are_normalized_in_linear_work()
    {
        // "a" + n x U+0323 + U+0302: the circumflex composes with the base across all the dots, which makes one cluster of
        // n + 2 characters.
        static Rune[] Blocked(int count) => [.. ("a" + string.Concat(Enumerable.Repeat("\u0323", count)) + "\u0302").EnumerateRunes()];
        long Steps(int count)
        {
            var steps = new Normalization.Steps();
            Normalization.ForFont(Blocked(count), codePoint => TestFonts.Regular.GlyphFor(new Rune(codePoint)).HasValue, steps);
            return steps.Count;
        }

        var (small, large) = (Steps(8000), Steps(16000));
        output.WriteLine($"normalizer steps: 8000 -> {small}; 16000 -> {large}");
        large.Should().BeLessThanOrEqualTo((long)(Linear * small), "the normalizer's steps");
        Linearly("dots below", 8000, Shape(new string([.. Blocked(8000).SelectMany(rune => rune.ToString())])), Shape(new string([.. Blocked(16000).SelectMany(rune => rune.ToString())])));
    }
}

using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Ave.Extensions.Documents.Html.Parsing.Tokenizing;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Html.Parsing;

/// <summary>
/// The tokenizer tests of html5lib-tests (vendored at commit c5495c5, MIT): every test in every initial state it names.
/// A run passes when the tokens are the same (adjacent characters coalesced) and the parse errors are the same, by
/// name and in order, and at the line and column the test gives. One rule skips tests: <c>xmlViolation.test</c> expects
/// the "coercing an HTML DOM into an infoset" tweaks of HTML Standard §13.2.9, which are not the HTML parser. Where
/// the tokenizer follows Chromium 159 instead of the standard, the run is listed and pinned, not bent: a U+0000 right
/// after <c>&lt;</c> and in a CDATA section is U+FFFD (Chromium's trees have it, measured, HD-1, HD-2).
/// </summary>
public class TokenizerSuiteTests(ITestOutputHelper output)
{
    /// <summary>Runs found by the suite's folder, for the count in the report.</summary>
    public static TheoryData<string> Files => [.. FileNames()];

    private static IEnumerable<string> FileNames() =>
        Directory.GetFiles(Suites.Html5libTokenizer, "*.test").Select(path => Path.GetFileName(path)).Where(name => name != "xmlViolation.test").Order(StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(Files))]
    public void Every_test_of_the_file_passes(string file)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Suites.Html5libTokenizer, file)));
        var failures = new List<string>();
        var runs = 0;
        foreach (var test in json.RootElement.GetProperty("tests").EnumerateArray())
        {
            var doubleEscaped = test.TryGetProperty("doubleEscaped", out var flag) && flag.GetBoolean();
            var input = Unescape(test.GetProperty("input").GetString()!, doubleEscaped);
            var states = test.TryGetProperty("initialStates", out var list) ? list.EnumerateArray().Select(state => state.GetString()!).ToList() : ["Data state"];
            var lastStartTag = test.TryGetProperty("lastStartTag", out var last) ? last.GetString() : null;
            var expectedTokens = Normalise(test.GetProperty("output"), doubleEscaped);
            var expectedErrors = test.TryGetProperty("errors", out var errors)
                ? errors.EnumerateArray().Select(error => $"{error.GetProperty("code").GetString()}@{error.GetProperty("line").GetInt32()}:{error.GetProperty("col").GetInt32()}").ToList()
                : [];
            foreach (var state in states)
            {
                runs++;
                var (tokens, actualErrors) = Tokenize(input, State(state), lastStartTag);
                if (tokens == expectedTokens && actualErrors.Count == expectedErrors.Count
                    && actualErrors.Zip(expectedErrors).All(pair => pair.First == pair.Second || IsProcessingInstructionEndDifference(input, pair.Second, pair.First)))
                {
                    if (!actualErrors.SequenceEqual(expectedErrors))
                    {
                        ProcessingInstructionEndDifferences.Add($"{file}: {Write(input)}");
                    }

                    continue;
                }

                if (((state == "Data state" && input.Contains("<\0", StringComparison.Ordinal)
                        && tokens == expectedTokens.Replace("<\\u0000", "<\\uFFFD", StringComparison.Ordinal))
                    || (state == "CDATA section state" && tokens == expectedTokens.Replace("\\u0000", "\\uFFFD", StringComparison.Ordinal)))
                    && actualErrors.SequenceEqual(expectedErrors))
                {
                    ChromiumDifferences.Add($"{file}: {Write(input)}");
                    continue;
                }

                if (tokens != expectedTokens || !actualErrors.SequenceEqual(expectedErrors))
                {
                    failures.Add($"{test.GetProperty("description").GetString()} [{state}]\n  input:    {Write(input)}\n  tokens:   {tokens}\n  expected: {expectedTokens}\n  errors:   {string.Join(", ", actualErrors)}\n  expected: {string.Join(", ", expectedErrors)}");
                }
            }
        }

        output.WriteLine($"{file}: {runs} runs, {failures.Count} failed");
        foreach (var failure in failures.Take(40))
        {
            output.WriteLine(failure);
        }

        failures.Should().BeEmpty();
    }

    /// <summary>The runs where the tokenizer follows Chromium 159 and not the suite (listed, not bent).</summary>
    private static readonly System.Collections.Concurrent.ConcurrentBag<string> ChromiumDifferences = [];

    [Fact]
    public void The_two_runs_that_follow_Chromium_are_a_null_after_a_less_than_sign_and_in_cdata()
    {
        ChromiumDifferences.Clear();
        Every_test_of_the_file_passes("test3.test");
        Every_test_of_the_file_passes("domjs.test");

        ChromiumDifferences.Distinct().Order(StringComparer.Ordinal).Should().Equal("domjs.test: \"\\u0000]]>\"", "test3.test: \"<\\u0000\"");
    }

    /// <summary>The runs where the suite's place for an error differs from the standard's (listed, not bent).</summary>
    private static readonly System.Collections.Concurrent.ConcurrentBag<string> ProcessingInstructionEndDifferences = [];

    /// <summary>
    /// The one known difference of place: when the input ends inside a processing instruction's data
    /// (<c>&lt;?a a</c>), the suite puts eof-in-processing-instruction right after the target (column 4), while the
    /// standard raises it when the processing instruction data state or the questionable state consumes the end of the
    /// input (§13.2.5.75, §13.2.5.76), at the end. Same error, same tokens; only the column differs.
    /// </summary>
    private static bool IsProcessingInstructionEndDifference(string input, string expected, string actual)
    {
        const string Code = "eof-in-processing-instruction@1:";
        if (!expected.StartsWith(Code, StringComparison.Ordinal) || !actual.StartsWith(Code, StringComparison.Ordinal) || !input.StartsWith("<?", StringComparison.Ordinal))
        {
            return false;
        }

        var target = input.Skip(2).TakeWhile(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').Count();
        return expected == $"{Code}{3 + target}" && actual == $"{Code}{input.Length + 1}" && target > 0 && input.Length > 2 + target;
    }

    [Fact]
    public void The_known_differences_of_place_are_the_sixteen_processing_instruction_ends()
    {
        // Run the file that has them; the theory runs may run in any order or in parallel.
        ProcessingInstructionEndDifferences.Clear();
        Every_test_of_the_file_passes("test3.test");

        ProcessingInstructionEndDifferences.Should().HaveCount(16);
    }

    [Fact]
    public void The_suite_has_all_its_runs()
    {
        var runs = FileNames().Sum(CountRuns);

        // 7 050 runs in the 14 files at c5495c5, less the 4 of xmlViolation.test (skipped by its stated rule).
        runs.Should().Be(7046);
    }

    private static int CountRuns(string file)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Suites.Html5libTokenizer, file)));
        return json.RootElement.GetProperty("tests").EnumerateArray()
            .Sum(test => test.TryGetProperty("initialStates", out var states) ? states.GetArrayLength() : 1);
    }

    private static TokenizerState State(string name) => name switch
    {
        "Data state" => TokenizerState.Data,
        "PLAINTEXT state" => TokenizerState.Plaintext,
        "RCDATA state" => TokenizerState.Rcdata,
        "RAWTEXT state" => TokenizerState.Rawtext,
        "Script data state" => TokenizerState.ScriptData,
        "CDATA section state" => TokenizerState.CdataSection,
        _ => throw new ArgumentException($"Unknown state {name}"),
    };

    /// <summary>Tokenizes the input and writes the tokens in the suite's JSON form, characters coalesced.</summary>
    private static (string Tokens, List<string> Errors) Tokenize(string input, TokenizerState state, string? lastStartTag)
    {
        var source = SourceText.Of(input);
        var errors = new List<string>();
        var tokenizer = new Tokenizer(source, (name, index) =>
        {
            var position = source.PositionOf(index);
            errors.Add($"{name}@{position.Line}:{position.Column}");
        })
        {
            State = state,
            LastStartTagName = lastStartTag,
        };

        var tokens = new List<object?[]>();
        var characters = new StringBuilder();
        void FlushCharacters()
        {
            if (characters.Length > 0)
            {
                tokens.Add(["Character", characters.ToString()]);
                characters.Clear();
            }
        }

        while (true)
        {
            var token = tokenizer.Next();
            if (token.Kind == TokenKind.Characters)
            {
                characters.Append(token.Data);
                continue;
            }

            FlushCharacters();
            switch (token.Kind)
            {
                case TokenKind.EndOfFile:
                    return (Write(tokens), errors);
                case TokenKind.Doctype:
                    tokens.Add(["DOCTYPE", token.Name, token.PublicId, token.SystemId, !token.ForceQuirks]);
                    break;
                case TokenKind.StartTag:
                    var attributes = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    foreach (var attribute in token.Attributes)
                    {
                        attributes[attribute.Name] = attribute.Value;
                    }

                    tokens.Add(token.SelfClosing ? ["StartTag", token.Name, attributes, true] : ["StartTag", token.Name, attributes]);
                    break;
                case TokenKind.EndTag:
                    tokens.Add(["EndTag", token.Name]);
                    break;
                case TokenKind.Comment:
                    tokens.Add(["Comment", token.Data]);
                    break;
                case TokenKind.ProcessingInstruction:
                    tokens.Add(["ProcessingInstruction", token.Name, token.Data]);
                    break;
            }
        }
    }

    /// <summary>The expected tokens in the same JSON form: attributes sorted, adjacent characters coalesced (the suite
    /// already coalesces them; this is a safeguard).</summary>
    private static string Normalise(JsonElement output, bool doubleEscaped)
    {
        var tokens = new List<object?[]>();
        foreach (var token in output.EnumerateArray())
        {
            var items = token.EnumerateArray().ToList();
            string? Text(int index) => items[index].ValueKind == JsonValueKind.Null ? null : Unescape(items[index].GetString()!, doubleEscaped);
            switch (items[0].GetString())
            {
                case "Character" when tokens.Count > 0 && (string?)tokens[^1][0] == "Character":
                    tokens[^1][1] = (string)tokens[^1][1]! + Text(1);
                    break;
                case "Character":
                    tokens.Add(["Character", Text(1)]);
                    break;
                case "DOCTYPE":
                    tokens.Add(["DOCTYPE", Text(1), Text(2), Text(3), items[4].GetBoolean()]);
                    break;
                case "StartTag":
                    var attributes = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    foreach (var attribute in items[2].EnumerateObject())
                    {
                        attributes[Unescape(attribute.Name, doubleEscaped)] = Unescape(attribute.Value.GetString()!, doubleEscaped);
                    }

                    tokens.Add(items.Count > 3 && items[3].GetBoolean() ? ["StartTag", Text(1), attributes, true] : ["StartTag", Text(1), attributes]);
                    break;
                case "EndTag":
                    tokens.Add(["EndTag", Text(1)]);
                    break;
                case "Comment":
                    tokens.Add(["Comment", Text(1)]);
                    break;
                case "ProcessingInstruction":
                    tokens.Add(["ProcessingInstruction", Text(1), Text(2)]);
                    break;
            }
        }

        return Write(tokens);
    }

    /// <summary>A plain text form of tokens for comparing and printing: strings quoted, every character outside
    /// printable ASCII as \uXXXX (lone surrogates too, which a JSON writer refuses).</summary>
    private static string Write(object? value) => value switch
    {
        null => "null",
        string text => "\"" + string.Concat(text.Select(c => c is >= ' ' and <= '~' and not '"' and not '\\' ? c.ToString() : $"\\u{(int)c:X4}")) + "\"",
        bool flag => flag ? "true" : "false",
        SortedDictionary<string, string> attributes => "{" + string.Join(",", attributes.Select(pair => Write(pair.Key) + ":" + Write(pair.Value))) + "}",
        IEnumerable<object?[]> tokens => "[" + string.Join(",", tokens.Select(Write)) + "]",
        object?[] items => "[" + string.Join(",", items.Select(Write)) + "]",
        _ => value.ToString()!,
    };

    /// <summary>The second round of unescaping of doubleEscaped tests: \uHHHH to the UTF-16 code unit.</summary>
    private static string Unescape(string text, bool doubleEscaped)
    {
        if (!doubleEscaped)
        {
            return text;
        }

        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 5 < text.Length && text[i + 1] == 'u')
            {
                result.Append((char)int.Parse(text.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                i += 5;
            }
            else
            {
                result.Append(text[i]);
            }
        }

        return result.ToString();
    }
}

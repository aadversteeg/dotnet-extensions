using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Css.Syntax;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Css;

/// <summary>
/// The syntax files of css-parsing-tests (vendored at commit 203ce36, CC0): each input against its expected result in
/// the suite's JSON form. The suite was written for the 2021 Candidate Recommendation of CSS Syntax 3; where the current
/// draft, which this parser and Chromium follow, says otherwise, the comparison is adapted by a stated rule and the
/// cases each rule touches are counted and pinned (<see cref="Every_adaptation_touches_the_cases_it_is_written_for"/>):
/// <list type="number">
/// <item><b>match tokens</b>: the 2021 text had the tokens <c>~=</c>, <c>|=</c>, <c>^=</c>, <c>$=</c>, <c>*=</c> and
/// <c>||</c>; the current draft has two delim tokens for each, and the expected value is split into them.</item>
/// <item><b>blocks as values</b>: the suite shows a rule's block as its component values; the parser keeps the block's
/// component values next to its parsed contents, and those are compared.</item>
/// <item><b>declaration lists drop at-rules</b>: "parse a list of declarations" (§5.4.5) consumes and drops at-rules;
/// the 2021 text returned them. The expected at-rules are left out.</item>
/// <item><b>a declaration ends at a semicolon</b>: "parse a declaration" (§5.4.7) consumes one declaration, which stops
/// at a semicolon, and fails when more input follows; the 2021 text put the semicolon and what follows into the value.
/// Inputs with a semicolon outside any block after the colon therefore expect a syntax error.</item>
/// <item><b>a declaration's value is trimmed</b>: the current draft drops white space after the colon and at the end
/// of the value (§5.5.6); the expected value is trimmed the same way.</item>
/// <item><b>unicode-range tokens only in the descriptor</b>: the 2021 tokenizer always made <c>U+…</c> a unicode-range
/// token; the current one does so only for the unicode-range descriptor (§4.3.1, §5.5.11). component_value_list.json
/// is tokenized with unicode ranges allowed.</item>
/// <item><b>no "list of rules"</b>: rule_list.json tests "parse a list of rules", an entry point the current draft no
/// longer has; its cases run as a style sheet, except the two that test that <c>&lt;!--</c> and <c>--&gt;</c> are not
/// special there (a style sheet discards them), which are skipped.</item>
/// </list>
/// Two parse errors are shown by the suite as values: <c>["error","eof-in-string"]</c> and <c>["error","eof-in-url"]</c>
/// after the token that the end of the input cut off; the runner adds them where this tokenizer reports those errors.
/// A number that is -0 is compared as 0 (JSON has one zero).
/// </summary>
public class CssParsingSuiteTests(ITestOutputHelper output)
{

    private static readonly string[] FileNames =
    [
        "component_value_list.json", "one_component_value.json", "declaration_list.json", "one_declaration.json",
        "blocks_contents.json", "one_rule.json", "rule_list.json", "stylesheet.json",
    ];

    public static TheoryData<string> Files => [.. FileNames];

    [Fact]
    public void Every_adaptation_touches_the_cases_it_is_written_for()
    {
        var counts = new Dictionary<string, int>();
        foreach (var file in FileNames)
        {
            RunFile(file, counts).Should().BeEmpty();
        }

        output.WriteLine(string.Join("\n", counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}: {pair.Value}")));
        counts.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["match tokens"] = 8,
            ["declaration lists drop at-rules"] = 5,
            ["a declaration ends at a semicolon"] = 2,
            ["a declaration's value is trimmed"] = 6,
            ["unicode-range tokens only in the descriptor"] = 9,
            ["no list of rules (skipped)"] = 2,
        });
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Every_case_of_the_file_passes(string file)
    {
        var failures = RunFile(file, []);
        foreach (var failure in failures)
        {
            output.WriteLine(failure);
        }

        failures.Should().BeEmpty();
    }

    /// <summary>
    /// stylesheet_bytes.json: the encoding chosen from the bytes, the protocol's label and the referring document's
    /// encoding (CSS Syntax 3 §3.2), for every case; and the rules of the decoded text, for every case the engine can
    /// decode. The others are in an encoding other than UTF-8 and UTF-16 with bytes outside ASCII: the engine refuses
    /// them (Unsupported.Encoding), and that is checked instead.
    /// </summary>
    [Fact]
    public void Stylesheet_bytes_choose_the_encoding_the_standard_says()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Suites.CssParsingTests, "stylesheet_bytes.json")));
        var items = json.RootElement.EnumerateArray().ToList();
        var (refused, failures) = (0, new List<string>());
        for (var i = 0; i < items.Count; i += 2)
        {
            var input = items[i];
            var bytes = Encoding.Latin1.GetBytes(input.GetProperty("css_bytes").GetString()!);
            string? Label(string name) => input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var encoding = CssDecoding.EncodingOf(bytes, Label("protocol_encoding"), Label("environment_encoding"));
            var expectedEncoding = items[i + 1][1].GetString()!;
            if (!string.Equals(encoding, expectedEncoding, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"#{i / 2}: encoding {encoding}, expected {expectedEncoding}");
                continue;
            }

            if (CssDecoding.Decode(bytes, encoding) is not { } text)
            {
                refused++;
                CssParser.ParseStyleSheet(bytes, new CssSourceOptions("bytes.css") { ProtocolEncoding = Label("protocol_encoding"), EnvironmentEncoding = Label("environment_encoding") })
                    .Diagnostics.Should().ContainSingle(error => error.Code == CssErrorCodes.Unsupported.Encoding);
                continue;
            }

            var actual = Canonical(JsonSerializer.SerializeToElement(SyntaxParser.ParseStyleSheet(CssSource.Of(text), (_, _) => { }).Rules.Select(RuleJson).ToList()));
            var expected = Canonical(items[i + 1][0]);
            if (actual != expected)
            {
                failures.Add($"#{i / 2}: expected {expected}\n  actual:   {actual}");
            }
        }

        output.WriteLine($"stylesheet_bytes.json: {items.Count / 2} cases, {items.Count / 2 - refused} decoded and compared, {refused} refused by rule (encoding not decoded, bytes outside ASCII), {failures.Count} failed");
        failures.ForEach(output.WriteLine);
        failures.Should().BeEmpty();
    }

    /// <summary>Runs every case of a file; returns the failures and counts the adaptations used.</summary>
    private static List<string> RunFile(string file, Dictionary<string, int> counts)
    {
        void Count(string rule) => counts[rule] = counts.GetValueOrDefault(rule) + 1;
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Suites.CssParsingTests, file)));
        var items = json.RootElement.EnumerateArray().ToList();
        var failures = new List<string>();
        for (var i = 0; i < items.Count; i += 2)
        {
            var input = items[i].GetString()!;
            if (file == "rule_list.json" && (input.Contains("<!--", StringComparison.Ordinal) || input.Contains("-->", StringComparison.Ordinal)))
            {
                Count("no list of rules (skipped)");
                continue;
            }

            var expected = Canonical(Adapt(file, input, items[i + 1], Count));
            var actual = Canonical(Run(file, input));
            if (actual != expected)
            {
                failures.Add($"{file} #{i / 2}: {Canonical(items[i])}\n  expected: {expected}\n  actual:   {actual}");
            }
        }

        failures.Insert(0, $"{file}: {items.Count / 2} cases, {failures.Count} failed");
        return failures.Count == 1 ? [] : failures;
    }

    // ---- the adaptations ----

    private static JsonElement Adapt(string file, string input, JsonElement expected, Action<string> Count)
    {
        var node = JsonSerializer.SerializeToNode(expected)!;
        node = SplitMatchTokens(node, Count);
        if (file == "declaration_list.json" && node is System.Text.Json.Nodes.JsonArray list)
        {
            var kept = new System.Text.Json.Nodes.JsonArray();
            foreach (var item in list)
            {
                if (item is System.Text.Json.Nodes.JsonArray { Count: > 0 } entry && Head(entry) == "at-rule")
                {
                    Count("declaration lists drop at-rules");
                    continue;
                }

                kept.Add(item?.DeepClone());
            }

            node = kept;
        }

        if (file == "one_declaration.json" && HasTopLevelSemicolonAfterColon(input))
        {
            Count("a declaration ends at a semicolon");
            node = new System.Text.Json.Nodes.JsonArray("error", "invalid");
        }
        else if (node is System.Text.Json.Nodes.JsonArray { Count: 4 } declaration && Head(declaration) == "declaration" && declaration[2] is System.Text.Json.Nodes.JsonArray value)
        {
            var before = value.Count;
            while (value.Count > 0 && value[0] is System.Text.Json.Nodes.JsonValue first && first.TryGetValue<string>(out var a) && a == " ")
            {
                value.RemoveAt(0);
            }

            while (value.Count > 0 && value[^1] is System.Text.Json.Nodes.JsonValue last && last.TryGetValue<string>(out var b) && b == " ")
            {
                value.RemoveAt(value.Count - 1);
            }

            if (value.Count != before)
            {
                Count("a declaration's value is trimmed");
            }
        }

        if (file == "component_value_list.json" && input.Contains("u+", StringComparison.OrdinalIgnoreCase))
        {
            Count("unicode-range tokens only in the descriptor");
        }

        return JsonSerializer.SerializeToElement(node);
    }

    private static System.Text.Json.Nodes.JsonNode SplitMatchTokens(System.Text.Json.Nodes.JsonNode node, Action<string> Count)
    {
        if (node is not System.Text.Json.Nodes.JsonArray array)
        {
            return node.DeepClone();
        }

        var result = new System.Text.Json.Nodes.JsonArray();
        foreach (var item in array)
        {
            if (item is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var text) && text is "~=" or "|=" or "^=" or "$=" or "*=" or "||")
            {
                Count("match tokens");
                result.Add(text[..1]);
                result.Add(text[1..]);
            }
            else
            {
                result.Add(item is null ? null : SplitMatchTokens(item, Count));
            }
        }

        return result;
    }

    private static bool HasTopLevelSemicolonAfterColon(string input)
    {
        var colon = input.IndexOf(':', StringComparison.Ordinal);
        return colon >= 0 && input.IndexOf(';', colon) > colon && !input[colon..].Contains('{', StringComparison.Ordinal);
    }

    // ---- running one case ----

    private static JsonElement Run(string file, string input)
    {
        var source = CssSource.Of(input);
        var endErrors = new List<string>();
        void Ignore(string name, int index)
        {
            if (name is "eof-in-string" or "eof-in-url")
            {
                endErrors.Add(name);
            }
        }

        object? result = file switch
        {
            "component_value_list.json" => Values(SyntaxParser.ParseComponentValues(source, Ignore, unicodeRangesAllowed: true).Values),
            "one_component_value.json" => OneComponentValue(SyntaxParser.ParseComponentValues(source, Ignore).Values),
            "declaration_list.json" => SyntaxParser.ParseDeclarationList(source, Ignore).Items
                .Select(item => item.Declaration is null ? new object[] { "error", "invalid" } : DeclarationJson(item.Declaration)).ToList(),
            "one_declaration.json" => SyntaxParser.ParseDeclaration(source, Ignore) switch
            {
                (null, true) => new object[] { "error", "empty" },
                (null, false) => new object[] { "error", "invalid" },
                ({ } declaration, _) => DeclarationJson(declaration),
            },
            "blocks_contents.json" => SyntaxParser.ParseBlockContents(source, Ignore).SelectMany(Items).ToList(),
            "one_rule.json" => SyntaxParser.ParseRule(source, Ignore) switch
            {
                (null, var error) => new object[] { "error", error! },
                ({ } rule, _) => RuleJson(rule),
            },
            _ => SyntaxParser.ParseStyleSheet(source, Ignore).Rules.Select(RuleJson).ToList(),
        };
        var node = JsonSerializer.SerializeToNode(result)!;
        foreach (var name in endErrors)
        {
            DeepestLastList(node)?.Add(new System.Text.Json.Nodes.JsonArray("error", name));
        }

        return JsonSerializer.SerializeToElement(node);
    }

    /// <summary>The first item of an array when it is a string (the kind of a value in the suite's form).</summary>
    private static string? Head(System.Text.Json.Nodes.JsonArray array) =>
        array.Count > 0 && array[0] is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>The list holding the last token in tree order: where the end of the input cut a string or URL off.</summary>
    private static System.Text.Json.Nodes.JsonArray? DeepestLastList(System.Text.Json.Nodes.JsonNode node) =>
        node is System.Text.Json.Nodes.JsonArray array ? Deepest(array) : null;

    private static System.Text.Json.Nodes.JsonArray Deepest(System.Text.Json.Nodes.JsonArray list)
    {
        if (list.Count > 0 && list[^1] is System.Text.Json.Nodes.JsonArray last)
        {
            switch (Head(last))
            {
                case "function" or "()" or "[]" or "{}":
                    return Deepest(last);
                case "qualified rule" or "declaration":
                    return Deepest((System.Text.Json.Nodes.JsonArray)last[2]!);
                case "at-rule":
                    return Deepest(last[3] as System.Text.Json.Nodes.JsonArray ?? (System.Text.Json.Nodes.JsonArray)last[2]!);
            }
        }

        return list;
    }

    private static object OneComponentValue(ValueList<ComponentValue> values)
    {
        var meaningful = values.Where(value => value is not CssToken { Kind: CssTokenKind.Whitespace }).ToList();
        return meaningful.Count switch
        {
            0 => new object[] { "error", "empty" },
            1 => Value(meaningful[0]),
            _ => new object[] { "error", "extra-input" },
        };
    }

    private static IEnumerable<object> Items(SyntaxBlockItem item) =>
        item.Rule is null ? item.Declarations.Select(DeclarationJson) : [RuleJson(item.Rule)];

    private static object DeclarationJson(Declaration declaration) =>
        new object[] { "declaration", declaration.Name, Values(declaration.Value), declaration.Important };

    private static object RuleJson(SyntaxRule rule) => rule switch
    {
        SyntaxAtRule at => new object?[] { "at-rule", at.Name, Values(at.Prelude), at.Block is null ? null : Values(at.Block.Values) },
        SyntaxQualifiedRule qualified => new object[] { "qualified rule", Values(qualified.Prelude), Values(qualified.Block.Values) },
        _ => new object[] { "error", "invalid" },
    };

    private static List<object> Values(IEnumerable<ComponentValue> values) => [.. values.Select(Value)];

    private static object Value(ComponentValue value) => value switch
    {
        SimpleBlock block => new object[] { block.Opening switch { CssTokenKind.OpenCurly => "{}", CssTokenKind.OpenSquare => "[]", _ => "()" } }.Concat(Values(block.Values)).ToArray(),
        FunctionValue function => new object[] { "function", function.Name }.Concat(Values(function.Values)).ToArray(),
        CssToken token => Token(token),
        _ => throw new InvalidOperationException(),
    };

    private static object Token(CssToken token) => token.Kind switch
    {
        CssTokenKind.Ident => new object[] { "ident", token.Text },
        CssTokenKind.AtKeyword => new object[] { "at-keyword", token.Text },
        CssTokenKind.Hash => new object[] { "hash", token.Text, token.IsIdHash ? "id" : "unrestricted" },
        CssTokenKind.String => new object[] { "string", token.Text },
        CssTokenKind.BadString => new object[] { "error", "bad-string" },
        CssTokenKind.Url => new object[] { "url", token.Text },
        CssTokenKind.BadUrl => new object[] { "error", "bad-url" },
        CssTokenKind.Delim => token.Text,
        CssTokenKind.Number => new object[] { "number", token.Representation, token.Number, token.IsInteger ? "integer" : "number" },
        CssTokenKind.Percentage => new object[] { "percentage", token.Representation, token.Number, token.IsInteger ? "integer" : "number" },
        CssTokenKind.Dimension => new object[] { "dimension", token.Representation, token.Number, token.IsInteger ? "integer" : "number", token.Text },
        CssTokenKind.UnicodeRange => new object[] { "unicode-range", token.RangeStart, token.RangeEnd },
        CssTokenKind.Whitespace => " ",
        CssTokenKind.Cdo => "<!--",
        CssTokenKind.Cdc => "-->",
        CssTokenKind.Colon => ":",
        CssTokenKind.Semicolon => ";",
        CssTokenKind.Comma => ",",
        CssTokenKind.CloseCurly => new object[] { "error", "}" },
        CssTokenKind.CloseSquare => new object[] { "error", "]" },
        CssTokenKind.CloseParen => new object[] { "error", ")" },
        _ => throw new InvalidOperationException(token.Kind.ToString()),
    };

    /// <summary>A canonical text of a JSON value: numbers by their double value, strings escaped one way.</summary>
    private static string Canonical(JsonElement element)
    {
        var text = new StringBuilder();
        void Write(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Array:
                    text.Append('[');
                    var first = true;
                    foreach (var item in e.EnumerateArray())
                    {
                        text.Append(first ? string.Empty : ",");
                        first = false;
                        Write(item);
                    }

                    text.Append(']');
                    break;
                case JsonValueKind.Object:
                    text.Append('{');
                    foreach (var property in e.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                    {
                        text.Append(property.Name).Append(':');
                        Write(property.Value);
                        text.Append(',');
                    }

                    text.Append('}');
                    break;
                case JsonValueKind.String:
                    text.Append('"');
                    foreach (var c in e.GetString()!)
                    {
                        text.Append(c is >= ' ' and <= '~' ? c.ToString() : $"\\u{(int)c:X4}");
                    }

                    text.Append('"');
                    break;
                case JsonValueKind.Number:
                    var number = e.GetDouble();
                    text.Append((number == 0 ? 0 : number).ToString("R", CultureInfo.InvariantCulture));
                    break;
                default:
                    text.Append(e.GetRawText());
                    break;
            }
        }

        Write(element);
        return text.ToString();
    }
}

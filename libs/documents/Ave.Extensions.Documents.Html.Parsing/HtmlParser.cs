using System;
using System.Collections.Generic;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Html.Parsing.Decoding;
using Ave.Extensions.Documents.Html.Parsing.Tokenizing;
using Ave.Extensions.Documents.Html.Parsing.TreeBuilding;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Html.Parsing;

/// <summary>
/// The HTML parser: the parsing algorithm of the HTML Standard (§13.2) with the scripting flag off. Every input gives a
/// tree, as in a browser; the parse errors the standard defines a recovery for are returned as diagnostics. Only the
/// limits (<see cref="HtmlInputLimits"/>) and the encoding rule make a parse fail.
/// </summary>
public static class HtmlParser
{
    /// <summary>Parses a whole document from text (no decoding; a <c>meta</c> charset is not looked at, as for a
    /// string given to a browser's <c>DOMParser</c>).</summary>
    /// <param name="text">The document.</param>
    /// <param name="options">The options, or null for none.</param>
    /// <returns>The document and its diagnostics, or <see cref="HtmlErrorCodes.TooLarge"/> or
    /// <see cref="HtmlErrorCodes.TooDeep"/>.</returns>
    public static Result<HtmlParseResult, Error> Parse(string text, HtmlParseOptions? options = null)
    {
        options ??= HtmlParseOptions.Default;
        return text.Length > HtmlInputLimits.MaxCharacters
            ? TooLarge(options)
            : ParseText(SourceText.Of(text), options, new Diagnostics(), checkMeta: false);
    }

    /// <summary>
    /// Parses a whole document from bytes: UTF-8, or UTF-16 when a byte order mark says so (the encoding sniffing
    /// algorithm of §13.2.3.1, step 1). Without a byte order mark the bytes are UTF-8, and a <c>meta</c> element that
    /// names another encoding — in the first 1024 bytes (§13.2.3.2) or in the head (§13.2.6.4.4) — makes the parse fail
    /// with <see cref="HtmlErrorCodes.Encoding"/>: a browser would decode the bytes differently, and the parser does not
    /// guess. Bytes that are not UTF-8 become U+FFFD, each with a diagnostic.
    /// </summary>
    /// <param name="bytes">The document's bytes.</param>
    /// <param name="options">The options, or null for none.</param>
    /// <returns>The document and its diagnostics, or an error.</returns>
    public static Result<HtmlParseResult, Error> Parse(ReadOnlyMemory<byte> bytes, HtmlParseOptions? options = null)
    {
        options ??= HtmlParseOptions.Default;
        if (bytes.Length > HtmlInputLimits.MaxCharacters)
        {
            return TooLarge(options);
        }

        var decoded = ByteDecoding.Decode(bytes.Span);
        var source = SourceText.Of(decoded.Text);
        var found = decoded.HasByteOrderMark ? null : MetaPrescan.Run(bytes.Span);
        if (found is not null && found.Encoding != EncodingLabels.Utf8)
        {
            var index = SourceText.NormalisedIndices(decoded.Text, [ByteDecoding.Utf8Length(bytes.Span, found.Offset)]);
            return EncodingError(options, found.Label, source.PositionOf(index.Count > 0 ? index[0] : 0));
        }

        // Bytes that are not UTF-8, with no byte order mark and nothing that declares UTF-8: a browser detects a legacy
        // encoding for them (Chromium 159 reads caf\xe9 as windows-1252 café, review 39 HD-16), the parser would make
        // U+FFFD of them. Refused, as a declared legacy encoding is.
        if (!decoded.HasByteOrderMark && found is null && decoded.Replacements.Count > 0)
        {
            var at = SourceText.NormalisedIndices(decoded.Text, [decoded.Replacements[0]]);
            return Result<HtmlParseResult, Error>.Failure(
                new Error(HtmlErrorCodes.Encoding, "The bytes are not UTF-8 and nothing declares an encoding: a browser would detect a legacy encoding for them, the parser reads UTF-8 and UTF-16 only. Convert the document to UTF-8, or declare it with <meta charset=\"utf-8\">.")
                    .With(ErrorMetadataKeys.Value, "(undeclared)")
                    .WithSource(options.SourceName, source.PositionOf(at.Count > 0 ? at[0] : 0)));
        }

        var replaced = new Diagnostics();
        foreach (var index in SourceText.NormalisedIndices(decoded.Text, decoded.Replacements))
        {
            replaced.Add(new Diagnostic("invalid-byte-sequence", index, null));
        }

        // The meta the prescan read is decided (it takes the last charset, as Chromium); tree construction checks the others.
        var decided = found is null ? -1 : SourceText.NormalisedIndices(decoded.Text, [ByteDecoding.Utf8Length(bytes.Span, found.Offset)]) is [var at0] ? at0 : -1;
        return ParseText(source, options, replaced, checkMeta: !decoded.HasByteOrderMark, decided);
    }

    /// <summary>The HTML fragment parsing algorithm (§13.4): the text as the content of a context element.</summary>
    /// <param name="text">The fragment.</param>
    /// <param name="context">The context element.</param>
    /// <param name="options">The options, or null for none.</param>
    /// <returns>The fragment and its diagnostics, or an error.</returns>
    public static Result<HtmlFragmentResult, Error> ParseFragment(string text, HtmlFragmentContext context, HtmlParseOptions? options = null)
    {
        options ??= HtmlParseOptions.Default;
        if (text.Length > HtmlInputLimits.MaxCharacters)
        {
            return Result<HtmlFragmentResult, Error>.Failure(TooLargeError(options));
        }

        var source = SourceText.Of(text);
        var diagnostics = new Diagnostics();
        var tokenizer = new Tokenizer(source, (name, index) => diagnostics.Add(new Diagnostic(name, index, null)));
        var builder = new TreeBuilder(tokenizer, (name, index, detail) => diagnostics.Add(new Diagnostic(name, index, detail)))
        {
            Position = source.PositionOf,
            InputLength = source.Length,
        };
        var fragment = builder.ParseFragment(context.Namespace, context.LocalName, context.Mode);
        if (builder.CloneBudgetExceeded)
        {
            return Result<HtmlFragmentResult, Error>.Failure(CloneBudgetError(options));
        }

        var freezer = new TreeFreezer(source.PositionOf);
        var frozen = freezer.Fragment(fragment);
        return (builder.TooDeepAt ?? freezer.TooDeepAt) is { } deep
            ? Result<HtmlFragmentResult, Error>.Failure(TooDeepError(options, source.PositionOf(deep.Start)))
            : Result<HtmlFragmentResult, Error>.Success(new HtmlFragmentResult(frozen, Errors(diagnostics, options, source)));
    }

    /// <summary>A parse error as tokenizer and tree construction report it.</summary>
    private sealed record Diagnostic(string Name, int Index, string? Detail);

    /// <summary>The diagnostics of one parse, at most <see cref="HtmlInputLimits.MaxDiagnostics"/>; the others are
    /// counted only.</summary>
    private sealed class Diagnostics
    {
        public List<Diagnostic> Kept { get; } = [];

        public int Suppressed { get; private set; }

        public void Add(Diagnostic diagnostic)
        {
            if (Kept.Count < HtmlInputLimits.MaxDiagnostics)
            {
                Kept.Add(diagnostic);
            }
            else
            {
                Suppressed++;
            }
        }
    }

    private static Result<HtmlParseResult, Error> ParseText(SourceText source, HtmlParseOptions options, Diagnostics diagnostics, bool checkMeta, int decidedMeta = -1)
    {
        var tokenizer = new Tokenizer(source, (name, index) => diagnostics.Add(new Diagnostic(name, index, null)));
        Error? encoding = null;
        var builder = new TreeBuilder(tokenizer, (name, index, detail) => diagnostics.Add(new Diagnostic(name, index, detail)))
        {
            Position = source.PositionOf,
            InputLength = source.Length,
            MetaEncoding = checkMeta ? meta => encoding ??= meta.Start == decidedMeta ? null : MetaEncodingError(meta, options, source) : null,
        };
        var document = builder.ParseDocument();
        if (encoding is { } failure)
        {
            return Result<HtmlParseResult, Error>.Failure(failure);
        }

        if (builder.CloneBudgetExceeded)
        {
            return Result<HtmlParseResult, Error>.Failure(CloneBudgetError(options));
        }

        var freezer = new TreeFreezer(source.PositionOf);
        var frozen = freezer.Document(document);
        return (builder.TooDeepAt ?? freezer.TooDeepAt) is { } deep
            ? Result<HtmlParseResult, Error>.Failure(TooDeepError(options, source.PositionOf(deep.Start)))
            : Result<HtmlParseResult, Error>.Success(new HtmlParseResult(frozen, Errors(diagnostics, options, source), options.SourceName));
    }

    /// <summary>The "in head" meta rule (§13.2.6.4.4) while the confidence is tentative: a charset, or an http-equiv
    /// content-type with a charset in its content, that names an encoding other than UTF-8.</summary>
    private static Error? MetaEncodingError(MutableElement meta, HtmlParseOptions options, SourceText source)
    {
        string? label = null;
        if (meta.Token.Attribute("charset") is { } charset && EncodingLabels.Resolve(charset) is not null)
        {
            label = charset;
        }
        else if (meta.Token.Attribute("http-equiv") is { } equiv && Ascii.EqualsIgnoreCase(equiv, "content-type")
                 && meta.Token.Attribute("content") is { } content && MetaPrescan.ContentCharset(content) is { } fromContent
                 && EncodingLabels.Resolve(fromContent) is not null)
        {
            label = fromContent;
        }

        return label is not null && EncodingLabels.ForMeta(EncodingLabels.Resolve(label)!) != EncodingLabels.Utf8
            ? EncodingError(options, label, source.PositionOf(meta.Start)).Error
            : null;
    }

    private static ValueList<Error> Errors(Diagnostics diagnostics, HtmlParseOptions options, SourceText source)
    {
        var errors = new List<Error>(diagnostics.Kept.Count + 1);
        foreach (var diagnostic in diagnostics.Kept)
        {
            var message = diagnostic.Detail is null
                ? $"HTML parse error '{diagnostic.Name}'."
                : $"HTML parse error '{diagnostic.Name}' at <{diagnostic.Detail}>.";
            errors.Add(new Error(HtmlErrorCodes.Parse.Syntax, message)
                .With(ErrorMetadataKeys.Value, diagnostic.Name)
                .WithSource(options.SourceName, source.PositionOf(diagnostic.Index)));
        }

        if (diagnostics.Suppressed > 0)
        {
            errors.Add(new Error(DocumentErrorCodes.TooManyDiagnostics, $"{diagnostics.Suppressed} more parse errors are left out: the parser keeps {HtmlInputLimits.MaxDiagnostics}.")
                .With(ErrorMetadataKeys.Value, diagnostics.Suppressed)
                .With(ErrorMetadataKeys.SourceFile, options.SourceName));
        }

        return [.. errors];
    }

    private static Result<HtmlParseResult, Error> TooLarge(HtmlParseOptions options) =>
        Result<HtmlParseResult, Error>.Failure(TooLargeError(options));

    private static Error TooLargeError(HtmlParseOptions options) =>
        new Error(HtmlErrorCodes.TooLarge, $"The text is longer than {HtmlInputLimits.MaxCharacters} characters, the most the HTML parser reads.")
            .With(ErrorMetadataKeys.Value, HtmlInputLimits.MaxCharacters)
            .With(ErrorMetadataKeys.SourceFile, options.SourceName);

    private static Error CloneBudgetError(HtmlParseOptions options) =>
        new Error(HtmlErrorCodes.TooLarge, $"The selected options' content cloned into selectedcontent elements exceeds {TreeBuilder.CloneWorkPerCharacter} nodes and characters per character of input.")
            .With(ErrorMetadataKeys.Value, TreeBuilder.CloneWorkPerCharacter)
            .With(ErrorMetadataKeys.SourceFile, options.SourceName);

    private static Error TooDeepError(HtmlParseOptions options, TextPosition position) =>
        new Error(HtmlErrorCodes.TooDeep, $"Elements are nested more than {HtmlInputLimits.MaxDepth} deep; the parser refuses the document instead of flattening it.")
            .With(ErrorMetadataKeys.Value, HtmlInputLimits.MaxDepth)
            .WithSource(options.SourceName, position);

    private static Result<HtmlParseResult, Error> EncodingError(HtmlParseOptions options, string label, TextPosition position) =>
        Result<HtmlParseResult, Error>.Failure(
            new Error(HtmlErrorCodes.Encoding, $"A meta element says the bytes are in the encoding '{label}'; the parser reads UTF-8 and UTF-16 only. Convert the document to UTF-8.")
                .With(ErrorMetadataKeys.Value, label)
                .WithSource(options.SourceName, position));
}

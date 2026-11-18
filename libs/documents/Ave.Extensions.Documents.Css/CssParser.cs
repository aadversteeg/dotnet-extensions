using System;
using System.Collections.Generic;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Media;
using Ave.Extensions.Documents.Css.Rules;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Css;

/// <summary>Where a style sheet's text comes from: its name for diagnostics, where it starts in its document, and the
/// encodings its bytes may be in.</summary>
/// <param name="SourceName">The name diagnostics carry as SourceFile (a file name or URL).</param>
public sealed record CssSourceOptions(string SourceName)
{
    /// <summary>Where the text starts in its document (a <c>style</c> element's or attribute's text in HTML), so that
    /// diagnostics point into the document; 1:1 for a style sheet of its own.</summary>
    public TextPosition Origin { get; init; } = TextPosition.Start;

    /// <summary>The encoding label the protocol gave (the charset of a Content-Type), or null (CSS Syntax 3 §3.2).</summary>
    public string? ProtocolEncoding { get; init; }

    /// <summary>The encoding of the document that refers to the style sheet, or null (CSS Syntax 3 §3.2, "environment
    /// encoding").</summary>
    public string? EnvironmentEncoding { get; init; }
}

/// <summary>
/// Reads CSS (CSS Syntax 3): style sheets into typed rules, and the declarations of a <c>style</c> attribute. Reading
/// never fails: what does not parse is dropped as CSS says, and reported. Input past the limits
/// (<see cref="CssInputLimits"/>) gives an empty result and a refusal.
/// </summary>
public static class CssParser
{
    /// <summary>Reads a style sheet from text.</summary>
    /// <param name="text">The style sheet.</param>
    /// <param name="options">Its name and place.</param>
    /// <returns>The style sheet and the diagnostics.</returns>
    public static CssResult<StyleSheet> ParseStyleSheet(string text, CssSourceOptions options)
    {
        var diagnostics = new List<Error>();
        if (text.Length > CssInputLimits.MaxCharacters)
        {
            return TooLarge(options);
        }

        var source = CssSource.Of(text, options.Origin);
        var (rules, tooDeepAt) = SyntaxParser.ParseStyleSheet(source, Syntax(source, options, diagnostics));
        if (tooDeepAt is { } deep)
        {
            return new CssResult<StyleSheet>(new StyleSheet([], options.SourceName), [.. diagnostics, TooDeep(source, deep, options)]);
        }

        var built = new RuleBuilder(options.SourceName, diagnostics).StyleSheet(rules);
        return new CssResult<StyleSheet>(new StyleSheet(built, options.SourceName), [.. diagnostics]);
    }

    /// <summary>
    /// Reads a style sheet from bytes (CSS Syntax 3 §3.2): a byte order mark decides the encoding; else the protocol's
    /// label, an <c>@charset</c> rule, the referring document's encoding, and UTF-8, in that order (labels that name no
    /// encoding are passed over). The engine decodes UTF-8 and UTF-16; bytes in another encoding are read only when
    /// they are all ASCII (every ASCII-compatible encoding reads those the same way), and refused otherwise.
    /// </summary>
    /// <param name="bytes">The style sheet's bytes.</param>
    /// <param name="options">Its name, place and encodings.</param>
    /// <returns>The style sheet and the diagnostics.</returns>
    public static CssResult<StyleSheet> ParseStyleSheet(ReadOnlyMemory<byte> bytes, CssSourceOptions options)
    {
        if (bytes.Length > 3L * CssInputLimits.MaxCharacters)
        {
            return TooLarge(options);
        }

        var encoding = CssDecoding.EncodingOf(bytes.Span, options.ProtocolEncoding, options.EnvironmentEncoding);
        if (CssDecoding.Decode(bytes.Span, encoding) is { } text)
        {
            return ParseStyleSheet(text, options);
        }

        return new CssResult<StyleSheet>(new StyleSheet([], options.SourceName), [
            new Error(CssErrorCodes.Unsupported.Encoding, $"The style sheet is in the encoding {encoding}, which the engine does not decode; only UTF-8 and UTF-16 are read.")
                .With(ErrorMetadataKeys.Value, encoding)
                .With(ErrorMetadataKeys.SourceFile, options.SourceName),
        ]);
    }

    /// <summary>"Parse a list of declarations" (CSS Syntax 3 §5.4.5) for a <c>style</c> attribute: the declarations
    /// that parse, in order. Their values are checked by the cascade, which reports the invalid ones.</summary>
    /// <param name="text">The attribute's value.</param>
    /// <param name="options">Its name and place.</param>
    /// <returns>The declarations and the diagnostics.</returns>
    public static CssResult<ValueList<Declaration>> ParseDeclarations(string text, CssSourceOptions options)
    {
        var diagnostics = new List<Error>();
        if (text.Length > CssInputLimits.MaxCharacters)
        {
            return new CssResult<ValueList<Declaration>>([], TooLarge(options).Diagnostics);
        }

        var source = CssSource.Of(text, options.Origin);
        var (items, tooDeepAt) = SyntaxParser.ParseDeclarationList(source, Syntax(source, options, diagnostics));
        if (tooDeepAt is { } deep)
        {
            return new CssResult<ValueList<Declaration>>([], [.. diagnostics, TooDeep(source, deep, options)]);
        }

        var declarations = new List<Declaration>();
        foreach (var (declaration, position) in items)
        {
            if (declaration is null)
            {
                diagnostics.Add(CssDiagnostics.At(CssErrorCodes.Parse.Syntax, "A declaration does not parse; it is dropped (CSS Syntax 3 §5.5.4).", options.SourceName, position));
            }
            else
            {
                declarations.Add(declaration);
            }
        }

        return new CssResult<ValueList<Declaration>>([.. declarations], [.. diagnostics]);
    }

    /// <summary>"Parse a media query list" (Media Queries 4 §3) from text: a media attribute of HTML, for one.</summary>
    /// <param name="text">The text.</param>
    /// <param name="options">Its name and place.</param>
    /// <returns>The list and the diagnostics.</returns>
    public static CssResult<MediaQueryList> ParseMediaQueryList(string text, CssSourceOptions options)
    {
        var diagnostics = new List<Error>();
        if (text.Length > CssInputLimits.MaxCharacters)
        {
            return new CssResult<MediaQueryList>(MediaQueryList.All, TooLarge(options).Diagnostics);
        }

        var source = CssSource.Of(text, options.Origin);
        var (values, tooDeepAt) = SyntaxParser.ParseComponentValues(source, Syntax(source, options, diagnostics));
        if (tooDeepAt is { } deep)
        {
            return new CssResult<MediaQueryList>(MediaQueryList.All, [.. diagnostics, TooDeep(source, deep, options)]);
        }

        var list = MediaQueries.Parse(values, options.SourceName, diagnostics);
        return new CssResult<MediaQueryList>(list, [.. diagnostics]);
    }

    private static Action<string, int> Syntax(CssSource source, CssSourceOptions options, List<Error> diagnostics) =>
        (message, index) => diagnostics.Add(CssDiagnostics.At(CssErrorCodes.Parse.Syntax, message, options.SourceName, source.PositionOf(index))
            .With(ErrorMetadataKeys.Value, message));

    private static CssResult<StyleSheet> TooLarge(CssSourceOptions options) =>
        new(new StyleSheet([], options.SourceName), [
            new Error(CssErrorCodes.TooLarge, $"The style sheet is longer than {CssInputLimits.MaxCharacters} characters; it is not read.")
                .With(ErrorMetadataKeys.Value, CssInputLimits.MaxCharacters)
                .With(ErrorMetadataKeys.SourceFile, options.SourceName),
        ]);

    private static Error TooDeep(CssSource source, int index, CssSourceOptions options) =>
        CssDiagnostics.At(CssErrorCodes.TooDeep, $"Blocks and functions are nested deeper than {CssInputLimits.MaxDepth}; the style sheet is not read.", options.SourceName, source.PositionOf(index))
            .With(ErrorMetadataKeys.Value, CssInputLimits.MaxDepth);
}

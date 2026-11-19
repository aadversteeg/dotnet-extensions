using System;
using System.Collections.Generic;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Media;
using Ave.Extensions.Documents.Css.Rules;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Css;

/// <summary>Where a style sheet's text comes from: its name for diagnostics, where it starts in its document, and the
/// encodings its bytes may be in.</summary>
/// <param name="SourceName">The name diagnostics carry as SourceFile (a file name or URL).</param>
public sealed record CssSourceOptions(string SourceName)
{
    /// <summary>Where the text starts in its document (a <c>style</c> element's or attribute's text in HTML), so that
    /// diagnostics point into the document; 1:1 for a style sheet of its own.</summary>
    public TextPosition Origin { get; init; } = TextPosition.Start;

    /// <summary>Where each character of the text was written in its document, when that is not one run from
    /// <see cref="Origin"/>: the value of an HTML attribute with character references or line feeds, a style element of
    /// several text nodes. Given an index of the text (0 to its length), its place. Null: counted from
    /// <see cref="Origin"/>.</summary>
    public Func<int, TextPosition>? Positions { get; init; }

    /// <summary>The encoding label the protocol gave (the charset of a Content-Type), or null (CSS Syntax 3 §3.2).</summary>
    public string? ProtocolEncoding { get; init; }

    /// <summary>The encoding of the document that refers to the style sheet, or null (CSS Syntax 3 §3.2, "environment
    /// encoding").</summary>
    public string? EnvironmentEncoding { get; init; }
}

/// <summary>
/// Reads CSS (CSS Syntax 3): style sheets into typed rules, and the declarations of a <c>style</c> attribute. What does
/// not parse is dropped as CSS says, and reported, so reading gives a result with its diagnostics. It fails only for
/// input past the limits (<see cref="CssInputLimits"/>: <see cref="CssErrorCodes.TooLarge"/>,
/// <see cref="CssErrorCodes.TooDeep"/>) and bytes it does not decode (<see cref="CssErrorCodes.Unsupported.Encoding"/>),
/// as <c>HtmlParser</c> fails for the same conditions: then there is no style sheet to use.
/// </summary>
public static class CssParser
{
    /// <summary>Reads a style sheet from text.</summary>
    /// <param name="text">The style sheet.</param>
    /// <param name="options">Its name and place.</param>
    /// <returns>The style sheet and the diagnostics, or the error past a limit.</returns>
    public static Result<CssResult<StyleSheet>, Error> ParseStyleSheet(string text, CssSourceOptions options)
    {
        var diagnostics = new DiagnosticCollector();
        if (text.Length > CssInputLimits.MaxCharacters)
        {
            return Fail<StyleSheet>(TooLarge(options));
        }

        var source = CssSource.Of(text, options.Origin, options.Positions);
        var (rules, tooDeepAt) = SyntaxParser.ParseStyleSheet(source, Syntax(source, options, diagnostics));
        if (tooDeepAt is { } deep)
        {
            return Fail<StyleSheet>(TooDeep(source, deep, options));
        }

        var built = new RuleBuilder(options.SourceName, diagnostics).StyleSheet(rules);
        var found = diagnostics.ToValueList();
        // A selector nested past the limit is a limit, not a diagnostic: the sheet is not read (as for blocks).
        foreach (var diagnostic in found)
        {
            if (diagnostic.Code == CssErrorCodes.TooDeep)
            {
                return Fail<StyleSheet>(diagnostic);
            }
        }

        return Result<CssResult<StyleSheet>, Error>.Success(new CssResult<StyleSheet>(new StyleSheet(built, options.SourceName), found));
    }

    /// <summary>The most bytes one UTF-16 code unit takes in UTF-8, so a style sheet of more bytes than this many times
    /// <see cref="CssInputLimits.MaxCharacters"/> is too large before it is decoded.</summary>
    internal const int MaxBytesPerCharacter = 3;

    /// <summary>
    /// Reads a style sheet from bytes (CSS Syntax 3 §3.2): a byte order mark decides the encoding; else the protocol's
    /// label, an <c>@charset</c> rule, the referring document's encoding, and UTF-8, in that order (labels that name no
    /// encoding are passed over). The engine decodes UTF-8 and UTF-16; bytes in another encoding are read only when
    /// they are all ASCII (every ASCII-compatible encoding reads those the same way), and refused otherwise.
    /// </summary>
    /// <param name="bytes">The style sheet's bytes.</param>
    /// <param name="options">Its name, place and encodings.</param>
    /// <returns>The style sheet and the diagnostics, or the error past a limit or for an encoding not decoded.</returns>
    public static Result<CssResult<StyleSheet>, Error> ParseStyleSheet(ReadOnlyMemory<byte> bytes, CssSourceOptions options)
    {
        if (bytes.Length > (long)MaxBytesPerCharacter * CssInputLimits.MaxCharacters)
        {
            return Fail<StyleSheet>(TooLarge(options));
        }

        var encoding = CssDecoding.EncodingOf(bytes.Span, options.ProtocolEncoding, options.EnvironmentEncoding);
        if (CssDecoding.Decode(bytes.Span, encoding) is { } text)
        {
            return ParseStyleSheet(text, options);
        }

        return Fail<StyleSheet>(new Error(CssErrorCodes.Unsupported.Encoding, $"The style sheet is in the encoding {encoding}, which the engine does not decode; only UTF-8 and UTF-16 are read.")
            .With(ErrorMetadataKeys.Value, encoding)
            .With(ErrorMetadataKeys.SourceFile, options.SourceName));
    }

    /// <summary>"Parse a list of declarations" (CSS Syntax 3 §5.4.5) for a <c>style</c> attribute: the declarations
    /// that parse, in order. Their values are checked by the cascade, which reports the invalid ones.</summary>
    /// <param name="text">The attribute's value.</param>
    /// <param name="options">Its name and place.</param>
    /// <returns>The declarations and the diagnostics, or the error past a limit.</returns>
    public static Result<CssResult<ValueList<Declaration>>, Error> ParseDeclarations(string text, CssSourceOptions options)
    {
        var diagnostics = new DiagnosticCollector();
        if (text.Length > CssInputLimits.MaxCharacters)
        {
            return Fail<ValueList<Declaration>>(TooLarge(options));
        }

        var source = CssSource.Of(text, options.Origin, options.Positions);
        var (items, tooDeepAt) = SyntaxParser.ParseDeclarationList(source, Syntax(source, options, diagnostics));
        if (tooDeepAt is { } deep)
        {
            return Fail<ValueList<Declaration>>(TooDeep(source, deep, options));
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

        return Result<CssResult<ValueList<Declaration>>, Error>.Success(new CssResult<ValueList<Declaration>>([.. declarations], diagnostics.ToValueList()));
    }

    /// <summary>"Parse a media query list" (Media Queries 4 §3) from text: a media attribute of HTML, for one.</summary>
    /// <param name="text">The text.</param>
    /// <param name="options">Its name and place.</param>
    /// <returns>The list and the diagnostics, or the error past a limit (a list that is not read matches nothing for a
    /// caller that goes on).</returns>
    public static Result<CssResult<MediaQueryList>, Error> ParseMediaQueryList(string text, CssSourceOptions options)
    {
        var diagnostics = new DiagnosticCollector();
        if (text.Length > CssInputLimits.MaxCharacters)
        {
            return Fail<MediaQueryList>(TooLarge(options));
        }

        var source = CssSource.Of(text, options.Origin, options.Positions);
        var (values, tooDeepAt) = SyntaxParser.ParseComponentValues(source, Syntax(source, options, diagnostics));
        if (tooDeepAt is { } deep)
        {
            return Fail<MediaQueryList>(TooDeep(source, deep, options));
        }

        var list = MediaQueries.Parse(values, options.SourceName, diagnostics);
        return Result<CssResult<MediaQueryList>, Error>.Success(new CssResult<MediaQueryList>(list, diagnostics.ToValueList()));
    }

    private static Result<CssResult<T>, Error> Fail<T>(Error error) => Result<CssResult<T>, Error>.Failure(error);

    private static Action<string, int> Syntax(CssSource source, CssSourceOptions options, DiagnosticCollector diagnostics) =>
        (message, index) => diagnostics.Add(CssDiagnostics.At(CssErrorCodes.Parse.Syntax, message, options.SourceName, source.PositionOf(index))
            .With(ErrorMetadataKeys.Value, message));

    private static Error TooLarge(CssSourceOptions options) =>
        new Error(CssErrorCodes.TooLarge, $"The style sheet is longer than {CssInputLimits.MaxCharacters} characters; it is not read.")
            .With(ErrorMetadataKeys.Value, CssInputLimits.MaxCharacters)
            .With(ErrorMetadataKeys.SourceFile, options.SourceName);

    private static Error TooDeep(CssSource source, int index, CssSourceOptions options) =>
        CssDiagnostics.At(CssErrorCodes.TooDeep, $"Blocks and functions are nested deeper than {CssInputLimits.MaxDepth}; the style sheet is not read.", options.SourceName, source.PositionOf(index))
            .With(ErrorMetadataKeys.Value, CssInputLimits.MaxDepth);
}

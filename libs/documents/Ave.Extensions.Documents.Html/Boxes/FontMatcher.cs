using System;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Cascade;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Css.Rules;
using Ave.Extensions.Documents.Css.Values;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.ErrorPaths.Functional;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Html.Boxes;

/// <summary>
/// Finds the face of an element's font (CSS Fonts 4 §5, font matching), from the document's <c>@font-face</c> rules
/// (§4) and the caller's fonts, and collects the faces it found for layout. The first family of the
/// <c>font-family</c> list that has any face is the family (§5.1; a generic family is a family nobody gives, in this
/// version); within it the face must have the computed weight and style exactly: there is no nearest-weight match and no
/// synthesis (§5.2 step 4), so a missing face is refused, not replaced. A <c>@font-face</c> rule takes part when its
/// family is the name and its weight range holds the weight; its first TrueType source is loaded, once, when a face is
/// first needed, through the caller's resolver, as Chromium loads a web font only when text needs it.
/// </summary>
internal sealed class FontMatcher(ValueList<FontFaceRule> faces, HtmlBoxOptions options, string sourceName)
{
    private readonly Dictionary<FontFaceRule, Result<TrueTypeFont, Error>> _loaded = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Result<TrueTypeFont, Error>> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Families, int Weight, bool Italic), Result<TrueTypeFont, Error>?> _matched = [];
    private readonly List<TrueTypeFont> _used = [];

    /// <summary>The faces found so far, in the order first used: the font set layout needs.</summary>
    public FontSet Used => new([.. _used]);

    /// <summary>The face for an element's computed font, or the refusal.</summary>
    /// <param name="style">The computed style.</param>
    /// <param name="position">The element's start tag, for the refusal.</param>
    /// <returns>The face, or the error.</returns>
    public Result<TrueTypeFont, Error> Match(ComputedStyle style, TextPosition position)
    {
        var families = style[PropertyRegistry.FontFamily] is CssList list ? list.Items : [];
        var weight = style[PropertyRegistry.FontWeight] is CssNumber number ? (int)number.Value : 400;
        var italic = style[PropertyRegistry.FontStyle] is CssKeyword { Name: "italic" };
        var text = string.Join(", ", families.Select(Name));
        var key = (text, weight, italic);
        if (!_matched.TryGetValue(key, out var match))
        {
            match = Find(families, weight, italic);
            _matched[key] = match;
            if (match is { IsSuccess: true } found && !_used.Contains(found.Value))
            {
                _used.Add(found.Value);
            }
        }

        if (match is { IsFailure: true } failed && failed.Error.Metadata?.ContainsKey(ErrorMetadataKeys.SourceLine) != true)
        {
            return Result<TrueTypeFont, Error>.Failure(failed.Error.WithSource(sourceName, position));
        }

        return match ?? Result<TrueTypeFont, Error>.Failure(
            new Error(HtmlStyleErrorCodes.Unsupported.Font, $"No family of 'font-family: {text}' gives a face of weight {weight}{(italic ? " italic" : string.Empty)}: give the font in a @font-face rule or in the fonts of the options (there are no system fonts).")
                .With(ErrorMetadataKeys.Value, text)
                .WithSource(sourceName, position));
    }

    /// <summary>The face of the first family that has any, or null when none has one; an error when that family's faces
    /// do not have the weight and style, or its font file is missing or broken.</summary>
    private Result<TrueTypeFont, Error>? Find(ValueList<CssValue> families, int weight, bool italic)
    {
        foreach (var family in families)
        {
            // Generic families (serif, sans-serif …) name no face in this version: the caller has no way to give one yet.
            if (family is not CssString { Value: var name })
            {
                continue;
            }

            var rules = faces.Where(face => string.Equals(face.Family, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (rules.Count > 0)
            {
                // CSS Fonts 4 §4.2: later rules for the same descriptors win.
                var face = rules.LastOrDefault(rule => rule.WeightRange.Low <= weight && weight <= rule.WeightRange.High && (rule.Style == "italic") == italic);
                return face is null
                    ? Result<TrueTypeFont, Error>.Failure(new Error(HtmlStyleErrorCodes.Unsupported.Font,
                            $"The @font-face rules of '{name}' have no face of weight {weight}{(italic ? " italic" : string.Empty)}; the nearest face is not taken and none is synthesised.")
                        .With(ErrorMetadataKeys.Value, name).WithSource(sourceName, rules[0].Position))
                    : Load(face);
            }

            var supplied = options.Fonts.Fonts.IsDefault ? [] : options.Fonts.Fonts;
            if (supplied.Any(font => font is not null && string.Equals(font.Names.Family, name, StringComparison.OrdinalIgnoreCase)))
            {
                return options.Fonts.Find(name, new FontWeight(weight), italic ? FontStyle.Italic : FontStyle.Normal);
            }
        }

        return null;
    }

    /// <summary>The font of a rule's first TrueType source (a format hint of truetype, or none), its URL resolved against
    /// the base of the rule's style sheet; loaded once per rule, and a file once per URL, also for two rules that name
    /// it.</summary>
    private Result<TrueTypeFont, Error> Load(FontFaceRule rule)
    {
        if (_loaded.TryGetValue(rule, out var loaded))
        {
            return loaded;
        }

        var source = rule.Sources.FirstOrDefault(candidate => candidate.Format is null or "truetype");
        var url = source is null ? null : Urls.Absolute(source.Url, rule.BaseUrl ?? options.BaseUrl) ?? source.Url;
        if (url is not null && _files.TryGetValue(url, out var file))
        {
            _loaded[rule] = file;
            return file;
        }

        var resource = url is null ? null : options.Resources?.Invoke(url);
        loaded = resource is null
            ? Result<TrueTypeFont, Error>.Failure(new Error(HtmlStyleErrorCodes.Resource.NotFound,
                    source is null ? $"The @font-face rule of '{rule.Family}' has no TrueType source." : $"The font file '{url}' of '{rule.Family}' was not given by the resolver.")
                .With(ErrorMetadataKeys.Value, url ?? rule.Family).WithSource(sourceName, rule.Position))
            : FontParser.Parse(resource.Bytes).MapError(error => error.WithSource(sourceName, rule.Position));
        _loaded[rule] = loaded;
        if (url is not null && loaded.IsSuccess)
        {
            _files[url] = loaded;
        }

        return loaded;
    }

    private static string Name(CssValue family) => family switch
    {
        CssString value => value.Value,
        CssKeyword keyword => keyword.Name,
        _ => family.ToString(),
    };
}

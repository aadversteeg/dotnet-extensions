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
/// <c>font-family</c> list that has any face is the family (§5.1); a generic family is the family the caller's
/// <see cref="GenericFamilies"/> map it to, looked up as a written name is, and one they do not map is refused, as a
/// browser would take a font of the system there; within the family the face must have the computed weight and style
/// exactly, or, where <c>font-synthesis-weight</c> or <c>font-synthesis-style</c> is <c>none</c>, be the nearest face of
/// another weight or style (§5.2 step 4, <see cref="Nearest"/>); there is no synthesis, so a face a browser would
/// synthesise is refused, not replaced.
/// The initial value (the registry's one value, which inherit, unset and initial pass on as it is; a written
/// "Times New Roman" is a value of its own) is the name "Times New Roman", as Chromium computes it, and then
/// <see cref="GenericFamilies.Standard"/>. A <c>@font-face</c> rule takes part when its family is the name and its
/// weight range holds the weight; its first TrueType source is loaded, once, when a face is first needed, through the
/// caller's resolver, as Chromium loads a web font only when text needs it.
/// </summary>
internal sealed class FontMatcher(ValueList<FontFaceRule> faces, HtmlBoxOptions options, string sourceName)
{
    private readonly Dictionary<FontFaceRule, Result<TrueTypeFont, Error>> _loaded = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Result<TrueTypeFont, Error>> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Families, bool Initial, Wanted Wanted), Result<TrueTypeFont, Error>> _matched = [];
    private readonly GenericFamilies _generics = options.GenericFamilies ?? GenericFamilies.None;
    private readonly List<TrueTypeFont> _used = [];

    /// <summary>The faces found so far, in the order first used: the font set layout needs.</summary>
    public FontSet Used => new([.. _used]);

    /// <summary>The face for an element's computed font, or the refusal.</summary>
    /// <param name="style">The computed style.</param>
    /// <param name="position">The element's start tag, for the refusal.</param>
    /// <param name="source">The name of the source <paramref name="position"/> is in, when it is not the document: the
    /// style sheet of the <c>content</c> declaration of a <c>::before</c> or <c>::after</c>.</param>
    /// <returns>The face, or the error.</returns>
    public Result<TrueTypeFont, Error> Match(ComputedStyle style, TextPosition position, string? source = null)
    {
        var value = style[PropertyRegistry.FontFamily];
        var families = value is CssList list ? list.Items : [];
        var initial = ReferenceEquals(value, PropertyRegistry.FontFamily.Initial);
        var wanted = WantedOf(style);
        var text = string.Join(", ", families.Select(Name));
        var key = (text, initial, wanted);
        if (!_matched.TryGetValue(key, out var match))
        {
            match = initial ? Initial(text, wanted) : Find(families, text, wanted);
            _matched[key] = match;
            if (match.IsSuccess && !_used.Contains(match.Value))
            {
                _used.Add(match.Value);
            }
        }

        return match.IsFailure && match.Error.Metadata?.ContainsKey(ErrorMetadataKeys.SourceLine) != true
            ? Result<TrueTypeFont, Error>.Failure(match.Error.WithSource(source ?? sourceName, position))
            : match;
    }

    /// <summary>
    /// For the refusal of a character a face has no glyph for: the family list in effect as an author wrote it, and the
    /// first family after the one that gave the face, in the same weight and style, whose face has the character (null
    /// when none has). CSS Fonts 4 §5 matches fonts per character, so a browser takes the character from that family; the
    /// engine does not, and names it.
    /// </summary>
    /// <param name="style">The computed style of the element.</param>
    /// <param name="face">The face <see cref="Match"/> gave it.</param>
    /// <param name="has">Whether a face has the character (the shaper's rule).</param>
    /// <returns>The list and the later family.</returns>
    public (string Families, string? Later) Describe(ComputedStyle style, TrueTypeFont face, Func<TrueTypeFont, bool> has)
    {
        var value = style[PropertyRegistry.FontFamily];
        if (ReferenceEquals(value, PropertyRegistry.FontFamily.Initial))
        {
            return ("the initial value of 'font-family'", null);
        }

        var families = value is CssList list ? list.Items : [];
        var wanted = WantedOf(style);
        var text = $"'font-family: {string.Join(", ", families.Select(Name))}'";
        var reached = false;
        foreach (var family in families)
        {
            var name = family switch
            {
                CssString written => written.Value,
                CssKeyword keyword => _generics.For(keyword.Name).Name,
                _ => null,
            };
            if (name is null || Named(name, wanted) is not { IsSuccess: true } found)
            {
                continue;
            }

            if (reached && has(found.Value))
            {
                return (text, name);
            }

            reached |= ReferenceEquals(found.Value, face);
        }

        return (text, null);
    }

    /// <summary>The face of the initial value: the name "Times New Roman" as a written name, then the family
    /// <see cref="GenericFamilies.Standard"/> maps it to.</summary>
    private Result<TrueTypeFont, Error> Initial(string text, Wanted wanted)
    {
        if (Named(text, wanted) is { } named)
        {
            return named;
        }

        var (name, property) = _generics.For("initial");
        return name is null
            ? Refused($"'font-family' is not set here, and its initial value needs a family: set {property} in the options, or give '{text}' in a @font-face rule or in the fonts of the options (a browser would take a font of the system; there are no system fonts).", text)
            : Named(name, wanted) ?? Refused($"The family '{name}' that {property} maps the initial value of 'font-family' to has no face: give it in a @font-face rule or in the fonts of the options.", text);
    }

    /// <summary>The face of the first family of a written list that has any (a generic family: the family it is mapped
    /// to), or the refusal.</summary>
    private Result<TrueTypeFont, Error> Find(ValueList<CssValue> families, string text, Wanted wanted)
    {
        foreach (var family in families)
        {
            switch (family)
            {
                case CssString { Value: var name }:
                    if (Named(name, wanted) is { } named)
                    {
                        return named;
                    }

                    break;
                case CssKeyword { Name: var keyword }:
                    var (mapped, property) = _generics.For(keyword);
                    return mapped is null
                        ? Refused($"'font-family: {text}' needs a family for the generic family '{keyword}': set {property} in the options (a browser would take a font of the system; there are no system fonts).", text)
                        : Named(mapped, wanted) ?? Refused($"The family '{mapped}' that {property} maps '{keyword}' to has no face: give it in a @font-face rule or in the fonts of the options.", text);
            }
        }

        return Refused($"No family of 'font-family: {text}' is supplied: give one in a @font-face rule or in the fonts of the options, or end the list with a generic family the options map (there are no system fonts).", text);
    }

    /// <summary>The face of a family name, from the document's <c>@font-face</c> rules first, then the caller's fonts;
    /// null when neither has the family; an error when its faces do not have the weight and style, or its font file is
    /// missing or broken.</summary>
    private Result<TrueTypeFont, Error>? Named(string name, Wanted wanted)
    {
        var (weight, italic) = (wanted.Weight, wanted.Italic);
        var rules = faces.Where(face => string.Equals(face.Family, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (rules.Count > 0)
        {
            // CSS Fonts 4 §4.2: later rules for the same descriptors win.
            var face = rules.LastOrDefault(rule => rule.WeightRange.Low <= weight && weight <= rule.WeightRange.High && (rule.Style == "italic") == italic)
                ?? Nearest([.. Enumerable.Reverse(rules).Select(rule => (rule.WeightRange.Low, rule.WeightRange.High, rule.Style == "italic", rule))], wanted);
            return face is null
                ? Result<TrueTypeFont, Error>.Failure(new Error(HtmlStyleErrorCodes.Unsupported.Font,
                        $"The @font-face rules of '{name}' have no face of weight {weight}{(italic ? " italic" : string.Empty)}; the nearest face is not taken and none is synthesised.{SynthesisHint}")
                    .With(ErrorMetadataKeys.Value, name).WithSource(sourceName, rules[0].Position))
                : Load(face);
        }

        var supplied = options.Fonts.Fonts.IsDefault ? [] : options.Fonts.Fonts;
        if (!supplied.Any(font => font is not null && string.Equals(font.Names.Family, name, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var found = options.Fonts.Find(name, new FontWeight(weight), italic ? FontStyle.Italic : FontStyle.Normal);
        if (found.IsSuccess || supplied.Any(font => font is null))
        {
            return found;
        }

        var family = supplied.Where(font => string.Equals(font.Names.Family, name, StringComparison.OrdinalIgnoreCase))
            .Select(font => (font.Metrics.WeightClass, font.Metrics.WeightClass, font.Metrics.IsItalic, font)).ToList();
        return Nearest(family, wanted) is { } nearest
            ? Result<TrueTypeFont, Error>.Success(nearest)
            : Result<TrueTypeFont, Error>.Failure(new Error(found.Error.Code, found.Error.Message + SynthesisHint, found.Error.Metadata));
    }

    /// <summary>What a refusal of a missing weight or style adds: the way out a browser's synthesis would take.</summary>
    private const string SynthesisHint = " Give the face, or write font-synthesis: none to print in the nearest face.";

    /// <summary>
    /// The face an element asks for: its computed weight and style, and whether <c>font-synthesis-weight</c> and
    /// <c>font-synthesis-style</c> are <c>none</c>.
    /// </summary>
    private readonly record struct Wanted(int Weight, bool Italic, bool AnyWeight, bool AnyStyle);

    private static Wanted WantedOf(ComputedStyle style) => new(
        style[PropertyRegistry.FontWeight] is CssNumber number ? (int)number.Value : 400,
        style[PropertyRegistry.FontStyle] is CssKeyword { Name: "italic" },
        style[SynthesisWeight] is CssKeyword { Name: "none" },
        style[SynthesisStyle] is CssKeyword { Name: "none" });

    private static readonly PropertyDefinition SynthesisWeight = PropertyRegistry.Find("font-synthesis-weight")!;
    private static readonly PropertyDefinition SynthesisStyle = PropertyRegistry.Find("font-synthesis-style")!;

    /// <summary>
    /// The nearest face of a family when it has none of the weight and style asked for (CSS Fonts 4 §5.2, step 4), as
    /// Chromium 159 prints it under <c>font-synthesis: none</c>, plainly (cases <c>fs-none-regular-only</c>,
    /// <c>fs-none-regular-bold</c>): taken only where the browser would otherwise synthesise, so a face of another style
    /// only under <c>font-synthesis-style: none</c>, and one of another weight only under <c>font-synthesis-weight:
    /// none</c>; otherwise null, and the face is refused. The style first: the faces of the style asked for, else of the
    /// other one. Then the weight, among those: one whose range holds it; else, for a weight from 400 to 500, the heavier
    /// ones up to 500 lightest first, then the lighter ones heaviest first, then those above 500 lightest first; below 400,
    /// the lighter ones heaviest first, then the heavier ones lightest first; above 500, the heavier ones lightest first,
    /// then the lighter ones heaviest first. Of faces at the same place, the first given.
    /// </summary>
    private static T? Nearest<T>(List<(int Low, int High, bool Italic, T Face)> family, Wanted wanted)
        where T : class
    {
        var styled = family.Where(face => face.Italic == wanted.Italic).ToList();
        if (styled.Count == 0 && wanted.AnyStyle)
        {
            styled = [.. family.Where(face => face.Italic != wanted.Italic)];
        }

        var exact = styled.FirstOrDefault(face => face.Low <= wanted.Weight && wanted.Weight <= face.High);
        if (exact.Face is not null)
        {
            return exact.Face;
        }

        if (styled.Count == 0 || !wanted.AnyWeight)
        {
            return null;
        }

        var w = wanted.Weight;
        var points = styled.Select(face => (Weight: Math.Clamp(w, face.Low, face.High), face.Face)).ToList();
        var order = w is >= 400 and <= 500
            ? points.Where(point => point.Weight >= w && point.Weight <= 500).OrderBy(point => point.Weight)
                .Concat(points.Where(point => point.Weight < w).OrderByDescending(point => point.Weight))
                .Concat(points.Where(point => point.Weight > 500).OrderBy(point => point.Weight))
            : w < 400
                ? points.Where(point => point.Weight <= w).OrderByDescending(point => point.Weight)
                    .Concat(points.Where(point => point.Weight > w).OrderBy(point => point.Weight))
                : points.Where(point => point.Weight >= w).OrderBy(point => point.Weight)
                    .Concat(points.Where(point => point.Weight < w).OrderByDescending(point => point.Weight));
        return order.Select(point => point.Face).FirstOrDefault();
    }

    /// <summary>A refusal of the family list, placed by <see cref="Match"/>.</summary>
    private static Result<TrueTypeFont, Error> Refused(string message, string text) =>
        Result<TrueTypeFont, Error>.Failure(new Error(HtmlStyleErrorCodes.Unsupported.Font, message).With(ErrorMetadataKeys.Value, text));

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

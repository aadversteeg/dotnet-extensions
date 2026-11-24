using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Css.Selectors;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.Documents.Css.Values;
using Ave.Extensions.Functional;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Css.Cascade;

/// <summary>
/// The text a <c>::before</c> or <c>::after</c> generates (CSS Pseudo 4 §4.1, CSS Generated Content 3 §1), with its
/// computed style.
/// </summary>
/// <param name="PseudoElement">Which one: <c>before</c> or <c>after</c>.</param>
/// <param name="Style">Its computed style, inheriting from its originating element.</param>
/// <param name="Text">The text of its strings and attribute values in order; may be empty (<c>content: ""</c>). When
/// the content reads counters, their text is added where the counters are counted, in document order
/// (<see cref="Items"/>).</param>
/// <param name="SourceName">The style sheet of the <c>content</c> declaration that won.</param>
/// <param name="Position">Where that declaration is written: the place reported for the generated text.</param>
public sealed record GeneratedContent(string PseudoElement, ComputedStyle Style, string Text, string SourceName, TextPosition Position)
{
    /// <summary>The items of the content in order: strings (an attribute's value read already) and the counters it
    /// reads (<see cref="CssContentNamedCounter"/>, <see cref="CssContentNamedCounters"/>). Each item is a text of its
    /// own in the line, as Chromium 159 draws it (measured: cases <c>gc-counter-styles</c> and <c>gc-items-kern</c>);
    /// without counters <see cref="Text"/> is the strings joined.</summary>
    public ValueList<CssContentItem> Items { get; init; } = [];
}

/// <summary>
/// The style of a list item's <c>::marker</c> (CSS Lists 3 §3.1), with what of it the engine draws.
/// </summary>
/// <param name="Style">Its computed style, inheriting from the list item; only its color, font-weight and font-style
/// differ from the item's, every other declaration of a <c>::marker</c> rule being refused.</param>
/// <param name="Text">The text of a <c>content</c> of strings, which replaces the marker's own text; null for the
/// marker the list style gives.</param>
public sealed record MarkerStyle(ComputedStyle Style, string? Text);

public sealed partial class StyleComputer
{
    private static readonly PropertyDefinition ContentProperty = PropertyRegistry.Find("content")!;

    /// <summary>The longhands a pseudo-element that generates text may not set to anything but what an inline child
    /// of its element has without them: they need an inline box, a block, a position or a float.</summary>
    private static readonly PropertyDefinition[] BoxLonghands = [.. PropertyRegistry.Longhands.Where(property =>
        property.Name.StartsWith("margin-", StringComparison.Ordinal) || property.Name.StartsWith("padding-", StringComparison.Ordinal)
        || property.Name.StartsWith("background-", StringComparison.Ordinal)
        || (property.Name.StartsWith("border-", StringComparison.Ordinal) && property.Name is not ("border-collapse" or "border-spacing"))
        || property.Name is "position" or "float")];

    /// <summary>For each pseudo-element name, whether any rule names it: without one, an element costs nothing more.</summary>
    private readonly Dictionary<string, bool> _pseudoRules = new(StringComparer.Ordinal);

    /// <summary>
    /// The generated text of an element's <c>::before</c> or <c>::after</c>, or null when it generates none. The rules
    /// whose selector ends in that pseudo-element and matches the element without it (Selectors 4 §3.6) are cascaded as
    /// an element's (origin and importance, layer, specificity counting the pseudo-element, order; CSS Cascade 5 §6.1),
    /// inheriting from the element's computed style. Nothing is generated, and nothing is reported, for
    /// <c>content: normal</c> or <c>none</c>, a CSS-wide keyword, no <c>content</c> at all (CSS Pseudo 4 §4.1; Chromium
    /// 159, measured: case <c>gc-nothing</c>), or <c>display: none</c>. A <c>display: block</c> one is a block box of its
    /// own, with its margins, padding, borders and background (measured: cases <c>gc-block-*</c>). Refused at its
    /// declaration, and not generated: a <c>content</c> item other than strings and <c>attr(name)</c>
    /// (<see cref="ContentValue.ParseGenerated"/>), more text than <see cref="CssInputLimits.MaxGeneratedTextLength"/>, an
    /// element that cannot hold it (<paramref name="refusedHost"/>), a <c>display</c> other than inline and block, a block
    /// one on an element that is not a block, and on an inline one a box property, position or float that differs from
    /// what an inline child of the element has without it.
    /// </summary>
    /// <param name="element">The originating element; its style must have been computed by this computer first.</param>
    /// <param name="pseudoElement"><c>before</c> or <c>after</c>.</param>
    /// <param name="refusedHost">Why the element cannot hold generated text, or null when it can: reported at the
    /// <c>content</c> declaration only when that generates text.</param>
    /// <returns>The generated text, null for none, or a limit's error.</returns>
    public Result<GeneratedContent?, Error> ComputePseudoElement(IStyleElement element, string pseudoElement, string? refusedHost = null)
    {
        if (_failure is not null)
        {
            return Result<GeneratedContent?, Error>.Failure(_failure.Value);
        }

        if (!_styles.TryGetValue(element, out var parent))
        {
            throw new InvalidOperationException("The element's style has not been computed: compute it before its pseudo-elements.");
        }

        if (!HasPseudoRules(pseudoElement))
        {
            return Result<GeneratedContent?, Error>.Success(null);
        }

        _element = element;
        var mark = ReportMark();
        if (Matched(element, pseudoElement) is not var (candidates, blocks))
        {
            return Result<GeneratedContent?, Error>.Failure(_failure!.Value);
        }

        var content = Content(candidates, element, pseudoElement);
        if (content is null && ReportMark() == mark)
        {
            return Result<GeneratedContent?, Error>.Success(null);
        }

        // content is read above, by its own grammar: the cascade of the other longhands never sees it.
        var contentMark = ReportMark();
        candidates.Longhands[ContentProperty.Index] = null!;
        foreach (var block in blocks)
        {
            Applied(block);
        }

        var computed = ComputeFrom(candidates, parent, isRoot: false, resetsLegacyAlignment: false);
        if (computed.IsFailure)
        {
            return Result<GeneratedContent?, Error>.Failure(computed.Error);
        }

        var style = computed.Value;
        var display = style[PropertyRegistry.Display];
        if (display is CssKeyword { Name: "none" })
        {
            // A pseudo-element of display: none generates no box (CSS Display 3 §2.5): nothing it says is reported,
            // its content not either.
            Rollback(mark);
            return Result<GeneratedContent?, Error>.Success(null);
        }

        if (content is not { } generated)
        {
            // The content is refused: that is the one thing reported for this pseudo-element.
            Rollback(contentMark);
            return Result<GeneratedContent?, Error>.Success(null);
        }

        var (text, items, declaration) = generated;
        if (refusedHost is not null)
        {
            Rollback(contentMark);
            Report(declaration, CssErrorCodes.Unsupported.GeneratedContent, $"'::{pseudoElement}' on <{element.LocalName}> is not drawn: {refusedHost}; the generated text is left out.");
            return Result<GeneratedContent?, Error>.Success(null);
        }

        var drawn = true;
        // What an inline child of the element has without the pseudo-element's own declarations: a margin of 0, no
        // border, static, no float. A declaration that keeps that value changes nothing and is drawn.
        var plain = ComputeFrom(new Candidates(), parent, isRoot: false, resetsLegacyAlignment: false);
        if (plain.IsFailure)
        {
            return Result<GeneratedContent?, Error>.Failure(plain.Error);
        }

        // A block pseudo-element is a block box: its margins, padding, borders and background are a block's (CSS 2.1 §8).
        var isBlock = display is CssKeyword { Name: "block" };
        foreach (var property in BoxLonghands)
        {
            if (style[property].Equals(plain.Value[property]) || (isBlock && property.Name is not ("position" or "float")))
            {
                continue;
            }

            var why = property.Name is "position" or "float"
                ? "generated text is drawn in the line of its element only"
                : "it needs inline boxes around generated text, which come in a later version";
            Report(DeclaredBy(candidates, property) ?? declaration, CssErrorCodes.Unsupported.GeneratedContent,
                $"'{property.Name}: {style[property]}' on ::{pseudoElement} is not drawn yet: {why}; remove it. The generated text is left out.");
            drawn = false;
        }

        // A position or a float makes the box a block (CSS 2.1 §9.7): that refusal says it already.
        if (drawn && display is not CssKeyword { Name: "inline" or "block" })
        {
            Report(DeclaredBy(candidates, PropertyRegistry.Display) ?? declaration, CssErrorCodes.Unsupported.GeneratedContent,
                $"'display: {display}' on ::{pseudoElement} is not drawn yet: only inline and block generated content is; the generated text is left out.");
            drawn = false;
        }

        // Chromium 159 breaks the inline element around a block inside it: its text before the block ends a line, the
        // block takes the next line, and the rest of its text starts the line after (measured: case gc-block-span).
        if (drawn && isBlock && parent[PropertyRegistry.Display] is var host && host is not CssKeyword { Name: "block" })
        {
            Report(DeclaredBy(candidates, PropertyRegistry.Display) ?? declaration, CssErrorCodes.Unsupported.GeneratedContent,
                $"'display: block' on ::{pseudoElement} of <{element.LocalName}> (display: {host}) is not drawn: a block pseudo-element is drawn on a block element only, and a browser breaks an inline element's lines around it; make the pseudo-element inline, or put it on a block element. The generated text is left out.");
            drawn = false;
        }

        return Result<GeneratedContent?, Error>.Success(drawn ? new GeneratedContent(pseudoElement, style, text, declaration.SourceName, declaration.Source.Position) { Items = items } : null);
    }

    /// <summary>The longhands a <c>::marker</c> rule draws besides <c>content</c> (measured: cases <c>gc-marker-color</c>,
    /// <c>gc-marker-font</c>).</summary>
    private static readonly FrozenSet<string> MarkerLonghands = new[] { "color", "font-weight", "font-style" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The style of a list item's <c>::marker</c> (CSS Lists 3 §3.1, CSS Pseudo 4 §4.2), or null when no rule names it:
    /// the rules whose selector ends in <c>::marker</c> and matches the element without it, cascaded as an element's,
    /// inheriting from the element's computed style. As Chromium 159 draws it (measured: cases <c>gc-marker-color</c>,
    /// <c>gc-marker-font</c>, <c>gc-marker-content</c>), a marker takes its <c>color</c>, <c>font-weight</c> and
    /// <c>font-style</c>, a shape as a number, and a <c>content</c> of strings replaces the marker's text. Refused at
    /// the declaration, and the marker drawn as if it were not written: any other property (a <c>font-size</c> of the
    /// marker changes the height of the item's first line in Chromium), <c>all</c>, a <c>content</c> that reads a counter
    /// or is <c>none</c>.
    /// </summary>
    /// <param name="element">The list item; its style must have been computed by this computer first.</param>
    /// <returns>The marker's style, null for none, or a limit's error.</returns>
    public Result<MarkerStyle?, Error> ComputeMarker(IStyleElement element)
    {
        if (_failure is not null)
        {
            return Result<MarkerStyle?, Error>.Failure(_failure.Value);
        }

        if (!_styles.TryGetValue(element, out var parent))
        {
            throw new InvalidOperationException("The element's style has not been computed: compute it before its pseudo-elements.");
        }

        if (!HasPseudoRules("marker"))
        {
            return Result<MarkerStyle?, Error>.Success(null);
        }

        _element = element;
        if (Matched(element, "marker") is not var (candidates, blocks))
        {
            return Result<MarkerStyle?, Error>.Failure(_failure!.Value);
        }

        if (blocks.Count == 0)
        {
            return Result<MarkerStyle?, Error>.Success(null);
        }

        foreach (var block in blocks)
        {
            Applied(block);
        }

        string? text = null;
        if (Highest(candidates.Longhands[ContentProperty.Index]) is { } winner)
        {
            var mark = ReportMark();
            var declaration = winner.Entry.Declaration;
            if (Content(candidates, element, "marker") is { } content)
            {
                if (content.Items.Any(item => item is CssContentNamedCounter or CssContentNamedCounters))
                {
                    Report(declaration, CssErrorCodes.Unsupported.GeneratedContent, "A counter in the content of ::marker is not drawn yet: write the numbering in list-style-type, or the text as a string.");
                }
                else
                {
                    text = content.Text;
                }
            }
            else if (ReportMark() == mark && string.Equals(CssSerializer.Shortened(DeclarationPreparer.Trim(declaration.Source.Value)), "none", StringComparison.OrdinalIgnoreCase))
            {
                Report(declaration, CssErrorCodes.Unsupported.GeneratedContent, "'content: none' on ::marker is not drawn yet: write list-style-type: none on the li.");
            }

            candidates.Longhands[ContentProperty.Index] = null!;
        }

        foreach (var property in PropertyRegistry.Longhands)
        {
            if (!MarkerLonghands.Contains(property.Name) && DeclaredBy(candidates, property) is { } declaration)
            {
                Report(declaration, CssErrorCodes.Unsupported.GeneratedContent,
                    $"'{property.Name}' on ::marker is not drawn yet: a list marker draws its color, font-weight, font-style and a content of strings only; remove the declaration, or set it on the li.");
                candidates.Longhands[property.Index] = null!;
            }
        }

        if (candidates.All is { Count: > 0 } all)
        {
            Report(all[0].Entry.Declaration, CssErrorCodes.Unsupported.GeneratedContent, "'all' on ::marker is not drawn yet: write the color, font-weight or font-style.");
            candidates.All.Clear();
        }

        var computed = ComputeFrom(candidates, parent, isRoot: false, resetsLegacyAlignment: false);
        return computed.IsFailure ? Result<MarkerStyle?, Error>.Failure(computed.Error) : Result<MarkerStyle?, Error>.Success(new MarkerStyle(computed.Value, text));
    }

    /// <summary>The candidates and blocks of the rules whose selector ends in the pseudo-element and matches the element
    /// without it (Selectors 4 §3.6), for a cascade as an element's (origin and importance, layer, specificity counting
    /// the pseudo-element, order; CSS Cascade 5 §6.1); null when the matching or the cascade budget is used up.</summary>
    private (Candidates Candidates, List<PreparedBlock> Blocks)? Matched(IStyleElement element, string pseudoElement)
    {
        var candidates = new Candidates();
        var blocks = new List<PreparedBlock>();
        foreach (var rule in _index.Candidates(element))
        {
            if (_matcher.Exhausted)
            {
                break;
            }

            if (!_matcher.MatchesPseudoElement(rule.Selector, element, pseudoElement))
            {
                continue;
            }

            _budget.Charge(1 + rule.Block.Entries.Count);
            blocks.Add(rule.Block);
            foreach (var entry in rule.Block.Entries)
            {
                var important = entry.Declaration.Important;
                candidates.Add(new Candidate(
                    entry,
                    new Priority(Priority.OriginRank(rule.Origin, important), 0, important ? -rule.Layer.Rank : rule.Layer.Rank, rule.Specificity, entry.Declaration.Order),
                    rule.Origin,
                    rule.Layer));
            }
        }

        if (_matcher.Exhausted || _budget.Exhausted)
        {
            Fail(new Error(CssErrorCodes.TooLarge, $"Styling the elements needs more than {_budget.MaxWork} steps of selector matching and cascading.")
                .With(ErrorMetadataKeys.Value, _budget.MaxWork));
            return null;
        }

        return (candidates, blocks);
    }

    /// <summary>Whether any rule's selector ends in this pseudo-element; looked at once per name.</summary>
    private bool HasPseudoRules(string pseudoElement)
    {
        if (!_pseudoRules.TryGetValue(pseudoElement, out var any))
        {
            any = _index.AllRules.Exists(rule => rule.Selector.Steps[^1].Compound.Selectors.Any(simple => simple is PseudoElementSelector named && named.Name == pseudoElement));
            _pseudoRules[pseudoElement] = any;
        }

        return any;
    }

    /// <summary>The declaration of the highest candidate of a longhand, or null when the pseudo-element declares none.</summary>
    private PreparedDeclaration? DeclaredBy(Candidates candidates, PropertyDefinition property) =>
        Highest(candidates.Longhands[property.Index]) is { } highest ? highest.Entry.Declaration : null;

    /// <summary>
    /// The text the winning <c>content</c> declaration generates, or null for none: the declaration highest in the
    /// cascade order among the property's own and those of <c>all</c>. <c>normal</c>, <c>none</c> and a CSS-wide
    /// keyword (<c>content</c> is not inherited, and the element's is <c>normal</c>) generate nothing; <c>revert</c>,
    /// <c>revert-layer</c>, <c>var()</c> and a value <see cref="ContentValue.ParseGenerated"/> refuses are reported
    /// and generate nothing.
    /// </summary>
    private (string Text, ValueList<CssContentItem> Items, PreparedDeclaration Declaration)? Content(Candidates candidates, IStyleElement element, string pseudoElement)
    {
        var (own, all) = (Highest(candidates.Longhands[ContentProperty.Index]), Highest(candidates.All));
        if (own is not { } winner || (all is { } shared && shared.Priority.CompareTo(winner.Priority) > 0))
        {
            return null;
        }

        var declaration = winner.Entry.Declaration;
        var value = declaration.Source.Value;
        if (DeclarationPreparer.WideKeyword(value) is { } keyword)
        {
            if (keyword is "revert" or "revert-layer")
            {
                Report(declaration, CssErrorCodes.Unsupported.GeneratedContent, $"'content: {keyword}' on ::{pseudoElement} is not drawn: write the value.");
            }

            return null;
        }

        if (DeclarationPreparer.ContainsFunction(value, "var"))
        {
            Report(declaration, CssErrorCodes.Unsupported.GeneratedContent, $"'content' with var() on ::{pseudoElement} is not drawn yet: write the value.");
            return null;
        }

        var (parsed, refused) = ContentValue.ParseGenerated(value);
        if (parsed is null)
        {
            Report(declaration, CssErrorCodes.Unsupported.GeneratedContent, $"'content: {CssSerializer.Shortened(DeclarationPreparer.Trim(value))}' on ::{pseudoElement}: {refused}.");
            return null;
        }

        if (!parsed.Generates)
        {
            return null;
        }

        var text = new StringBuilder();
        var items = new List<CssContentItem>(parsed.Items.Count);
        foreach (var item in parsed.Items)
        {
            if (item is CssContentNamedCounter or CssContentNamedCounters)
            {
                items.Add(item);
                continue;
            }

            var piece = item switch
            {
                CssContentString literal => literal.Value,
                CssContentAttribute attribute => element.GetAttribute(element.IsHtml ? InfraAscii.ToLower(attribute.Name) : attribute.Name) ?? string.Empty,
                _ => string.Empty,
            };
            items.Add(new CssContentString(piece));
            _budget.Charge(CascadeBudget.CharactersCost(piece.Length));
            if (text.Length + piece.Length > CssInputLimits.MaxGeneratedTextLength)
            {
                Report(declaration, CssErrorCodes.Unsupported.GeneratedContent,
                    $"The content of ::{pseudoElement} on <{element.LocalName}> makes more than {CssInputLimits.MaxGeneratedTextLength} characters of text; it is not drawn: shorten the strings or the attribute.");
                return null;
            }

            text.Append(piece);
        }

        return (text.ToString(), [.. items], declaration);
    }
}

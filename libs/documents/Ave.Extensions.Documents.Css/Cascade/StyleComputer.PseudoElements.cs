using System;
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
/// <param name="Text">The text: its strings and attribute values in order; may be empty (<c>content: ""</c>).</param>
/// <param name="SourceName">The style sheet of the <c>content</c> declaration that won.</param>
/// <param name="Position">Where that declaration is written: the place reported for the generated text.</param>
public sealed record GeneratedContent(string PseudoElement, ComputedStyle Style, string Text, string SourceName, TextPosition Position);

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
    /// 159, measured: case <c>gc-nothing</c>), or <c>display: none</c>. Refused at its declaration, and not generated: a
    /// <c>content</c> item other than strings and <c>attr(name)</c> (<see cref="ContentValue.ParseGenerated"/>), more
    /// text than <see cref="CssInputLimits.MaxGeneratedTextLength"/>, an element that cannot hold it
    /// (<paramref name="refusedHost"/>), a <c>display</c> other than inline, and a box property, position or float
    /// that differs from what an inline child of the element has without it.
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
            return Result<GeneratedContent?, Error>.Failure(_failure!.Value);
        }

        if (Content(candidates, element, pseudoElement) is not { } content)
        {
            return Result<GeneratedContent?, Error>.Success(null);
        }

        var (text, declaration) = content;
        if (refusedHost is not null)
        {
            Report(declaration, CssErrorCodes.Unsupported.GeneratedContent, $"'::{pseudoElement}' on <{element.LocalName}> is not drawn: {refusedHost}; the generated text is left out.");
            return Result<GeneratedContent?, Error>.Success(null);
        }

        // content is read above, by its own grammar: the cascade of the other longhands never sees it.
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

        foreach (var property in BoxLonghands)
        {
            if (style[property].Equals(plain.Value[property]))
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
        if (drawn && display is not CssKeyword { Name: "inline" })
        {
            Report(DeclaredBy(candidates, PropertyRegistry.Display) ?? declaration, CssErrorCodes.Unsupported.GeneratedContent,
                $"'display: {display}' on ::{pseudoElement} is not drawn yet: only inline generated text is, a block pseudo-element comes in a later version; the generated text is left out.");
            drawn = false;
        }

        return Result<GeneratedContent?, Error>.Success(drawn ? new GeneratedContent(pseudoElement, style, text, declaration.SourceName, declaration.Source.Position) : null);
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
    private (string Text, PreparedDeclaration Declaration)? Content(Candidates candidates, IStyleElement element, string pseudoElement)
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
        foreach (var item in parsed.Items)
        {
            var piece = item switch
            {
                CssContentString literal => literal.Value,
                CssContentAttribute attribute => element.GetAttribute(element.IsHtml ? InfraAscii.ToLower(attribute.Name) : attribute.Name) ?? string.Empty,
                _ => string.Empty,
            };
            _budget.Charge(CascadeBudget.CharactersCost(piece.Length));
            if (text.Length + piece.Length > CssInputLimits.MaxGeneratedTextLength)
            {
                Report(declaration, CssErrorCodes.Unsupported.GeneratedContent,
                    $"The content of ::{pseudoElement} on <{element.LocalName}> makes more than {CssInputLimits.MaxGeneratedTextLength} characters of text; it is not drawn: shorten the strings or the attribute.");
                return null;
            }

            text.Append(piece);
        }

        return (text.ToString(), declaration);
    }
}

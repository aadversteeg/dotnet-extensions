using System;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Cascade;
using Ave.Extensions.Documents.Css.Values;

namespace Ave.Extensions.Documents.Css.Properties;

/// <summary>
/// What computing one element's values needs (CSS Cascade 5 §4.4): its parent's computed style, its own values computed
/// so far (font-size first, so <c>em</c> can resolve), the root's font size for <c>rem</c>, and the caller's value
/// context.
/// </summary>
internal sealed class ComputeContext
{
    private readonly CssValue?[] _own;

    private readonly double _rootFontSize;

    /// <summary>Starts computing one element: <paramref name="parent"/> is null for the root, which inherits the
    /// initial values.</summary>
    public ComputeContext(ComputedStyle? parent, double rootFontSize, ValueContext values, bool isRoot)
    {
        Parent = parent;
        _rootFontSize = rootFontSize;
        Values = values;
        IsRoot = isRoot;
        _own = new CssValue?[PropertyRegistry.Longhands.Count];
    }

    /// <summary>The parent's computed style, or null for the root.</summary>
    public ComputedStyle? Parent { get; }

    /// <summary>Per side (top, right, bottom, left), the border width the element holds behind a border style of none or
    /// hidden, whose computed width is zero; null where the style shows the width (see
    /// <see cref="ComputedStyle.WidthBehindStyle"/>).</summary>
    public CssValue?[]? WidthsBehindStyle { get; private set; }

    /// <summary>Keeps the width a border style of none or hidden makes zero, for an <c>inherit</c> of a child.</summary>
    /// <param name="side">The side's index: top 0, right 1, bottom 2, left 3.</param>
    /// <param name="width">The width as computed without the style rule.</param>
    public void KeepWidthBehindStyle(int side, CssValue width) => (WidthsBehindStyle ??= new CssValue?[4])[side] = width;

    /// <summary>The root element's computed font size in points, for <c>rem</c> (CSS Values 4 §6.1.1); for the root
    /// itself its own, once computed.</summary>
    public double RootFontSize => IsRoot ? FontSize : _rootFontSize;

    /// <summary>The caller's viewport and fonts.</summary>
    public ValueContext Values { get; }

    /// <summary>Whether the element is the root element.</summary>
    public bool IsRoot { get; }

    /// <summary>The parent's computed font size in points (the initial 12pt for the root).</summary>
    public double ParentFontSize => Parent is { } parent ? ((CssLength)parent[PropertyRegistry.FontSize]).Points : PropertyRegistry.InitialFontSizePoints;

    /// <summary>The element's own computed font size in points; computed before every other property.</summary>
    public double FontSize => _own[PropertyRegistry.FontSize.Index] is CssLength size ? size.Points : ParentFontSize;

    /// <summary>Where the element's computed font size comes from, set when font-size is computed.</summary>
    public FontSizeSource FontSizeSource { get; set; } = FontSizeSource.Medium;

    /// <summary>Where the parent's font size comes from; medium for the root (its parent is the initial value).</summary>
    public FontSizeSource ParentFontSizeSource => Parent is { } parent ? parent.FontSizeSource : FontSizeSource.Medium;

    /// <summary>Whether the parent's computed font family is monospace alone (false for the root).</summary>
    public bool ParentIsMonospace => Parent is { } parent && IsMonospaceFamily(parent[PropertyRegistry.FontFamily]);

    /// <summary>Whether the element's computed font family is the one generic family monospace, written without quotes
    /// (font-family is computed before font-size).</summary>
    public bool IsMonospace => IsMonospaceFamily(Own(PropertyRegistry.FontFamily));

    private static bool IsMonospaceFamily(CssValue? family) => family is CssList { Items: [CssKeyword { Name: "monospace" }] };

    /// <summary>The element's own computed value of a property computed earlier, or null.</summary>
    public CssValue? Own(PropertyDefinition property) => _own[property.Index];

    /// <summary>Records a computed value of the element.</summary>
    public void Set(PropertyDefinition property, CssValue value) => _own[property.Index] = value;

    /// <summary>The parent's computed value of a property, or its initial value for the root.</summary>
    public CssValue Inherited(PropertyDefinition property) => Parent is { } parent ? parent[property] : property.Initial;

    /// <summary>A relative length in points (CSS Values 4 §6.1): em, rem, ex, ch, vw, vh. For font-size itself, em and ex
    /// refer to the parent's font (§6.1.1). Null when ex or ch is needed and the caller has no font metrics.</summary>
    public double? Points(double value, string unit, bool forFontSize)
    {
        var fontSize = forFontSize ? ParentFontSize : FontSize;
        switch (unit)
        {
            case "em":
                return value * fontSize;
            case "rem":
                return value * (IsRoot && forFontSize ? PropertyRegistry.InitialFontSizePoints : RootFontSize);
            case "vw":
                return value * Values.ViewportWidth.Points / 100;
            case "vh":
                return value * Values.ViewportHeight.Points / 100;
            case "ex" or "ch":
                var style = forFontSize ? Parent : null;
                var families = FamiliesOf(forFontSize ? Parent?[PropertyRegistry.FontFamily] : Own(PropertyRegistry.FontFamily) ?? Inherited(PropertyRegistry.FontFamily));
                var weight = (forFontSize ? style?[PropertyRegistry.FontWeight] : Own(PropertyRegistry.FontWeight)) is CssNumber number ? number.Value : 400;
                var italic = (forFontSize ? style?[PropertyRegistry.FontStyle] : Own(PropertyRegistry.FontStyle)) is CssKeyword { Name: "italic" };
                if (Values.FontMetrics?.Invoke(families, weight, italic, fontSize) is not { } metrics)
                {
                    return null;
                }

                return value * (unit == "ex" ? metrics.XHeight : metrics.ZeroAdvance);
            default:
                return Units.PointsPer(unit) is { } points ? value * points : null;
        }
    }

    private static ValueList<string> FamiliesOf(CssValue? value) => value is CssList list
        ? [.. System.Linq.Enumerable.Select(list.Items, item => item switch { CssString s => s.Value, CssKeyword k => k.Name, _ => item.ToString() })]
        : [];

    /// <summary>
    /// The computed value of a &lt;length-percentage&gt; (CSS Values 4 §6, §10.10): an absolute length in points; a
    /// percentage stays a percentage unless <paramref name="percentBasis"/> is known (font-size, line-height); a calc()
    /// is resolved, and refused when a length and a percentage of something not yet known stay mixed (the document
    /// model's length cannot carry both).
    /// </summary>
    public ComputedResult LengthPercentage(CssValue value, bool forFontSize = false, double? percentBasis = null) =>
        InRange(Unbounded(value, forFontSize, percentBasis));

    /// <summary>A computed length or percentage the engine keeps: finite, and at most
    /// <see cref="CssInputLimits.MaxLengthPixels"/> px or percent either way; past that, refused.</summary>
    private static ComputedResult InRange(ComputedResult result) => result.Value switch
    {
        CssLength length when !(Math.Abs(length.Points / 0.75) <= CssInputLimits.MaxLengthPixels) =>
            ComputedResult.Refused($"a length beyond {CssInputLimits.MaxLengthPixels} px either way (Chromium 159 clamps lengths near 3.36e7 px)"),
        CssPercentage percentage when !(Math.Abs(percentage.Value) <= CssInputLimits.MaxLengthPixels) =>
            ComputedResult.Refused($"a percentage beyond {CssInputLimits.MaxLengthPixels}% either way"),
        _ => result,
    };

    private ComputedResult Unbounded(CssValue value, bool forFontSize, double? percentBasis)
    {
        switch (value)
        {
            case CssLength:
                return value;
            case CssDimension dimension:
                return Points(dimension.Value, dimension.Unit, forFontSize) is { } points
                    ? new CssLength(Length.FromPoints(points))
                    : ComputedResult.Refused($"the unit '{dimension.Unit}' needs font metrics, and none were given");
            case CssPercentage percentage:
                return percentBasis is { } basis ? new CssLength(Length.FromPoints(percentage.Value * basis / 100)) : value;
            case CssCalc calc:
                var unresolved = false;
                var result = Calc.Evaluate(calc.Expression, (number, unit) =>
                {
                    var resolved = Points(number, unit, forFontSize);
                    unresolved |= resolved is null;
                    return resolved;
                }, percentBasis);
                if (unresolved)
                {
                    return ComputedResult.Refused("a unit in calc() needs font metrics, and none were given");
                }

                if (result is not { } r)
                {
                    return ComputedResult.Refused("min(), max() or clamp() compare a length with a percentage of something not yet known");
                }

                if (r.Percent != 0 && r.Points != 0)
                {
                    return ComputedResult.Refused("a calc() that mixes a length and a percentage after computing is not supported");
                }

                return r.Percent != 0 ? new CssPercentage(r.Percent) : new CssLength(Length.FromPoints(r.Points));
            default:
                return value;
        }
    }
}

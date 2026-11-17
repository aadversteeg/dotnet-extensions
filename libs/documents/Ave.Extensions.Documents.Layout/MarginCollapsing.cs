using System;
using System.Linq;
using Ave.Documents.DocumentModel;

namespace Ave.Documents.Layout;

/// <summary>
/// Collapsing margins of block flow, CSS 2.1 §8.3.1: "adjoining margins of two or more boxes … can combine to
/// form a single margin". Pure functions of the document model; <see cref="FlowLayout"/> uses them while it
/// stacks children, so the collapsing happens inside the flow algorithm itself (architecture: it interacts with
/// fragmentation, so it cannot be a separate policy).
/// </summary>
/// <remarks>
/// <para>Which margins adjoin (§8.3.1, "Two margins are adjoining if and only if …"), in the terms of this model:</para>
/// <list type="bullet">
/// <item>the bottom margin of a box and the top margin of its next sibling;</item>
/// <item>the top margin of a flow box and the top margin of its first child, when no top padding and no top border
/// separate them ("no line boxes, no clearance, no padding and no border separate them");</item>
/// <item>the bottom margin of the last child and the bottom margin of its flow parent, when no bottom padding and no
/// bottom border separate them (the model has no <c>height</c> property, so every height is <c>auto</c> as the rule
/// needs);</item>
/// <item>the top and bottom margins of an EMPTY box (<see cref="IsEmpty"/>), which therefore collapse "through" it
/// with whatever adjoins either side.</item>
/// </list>
/// <para>Adjoining is transitive, so each function below folds a whole chain of adjoining margins into one value.</para>
/// <para>LIMITS of this version, each an explicit rule rather than an omission: margins are never negative (the
/// front-ends refuse them, error <c>Documents.Input.Unsupported.NegativeMargin</c>), so "the maximum of the
/// collapsing margins' widths" (§8.3.1) is the whole rule and the negative-margin arithmetic of §8.3.1 is not needed. Borders are
/// checked like padding (a border of zero width is no border). The model has no <c>min-height</c>, no floats and
/// therefore no clearance, so the conditions of §8.3.1 that mention them always hold; when one of those properties
/// arrives, its condition belongs in <see cref="TopAdjoinsChildren"/>, <see cref="BottomAdjoinsChildren"/> and
/// <see cref="IsEmpty"/>.</para>
/// <para>Boxes laid out by an algorithm other than Flow or Inline are treated like boxes that establish their own
/// formatting context: their margins collapse with their siblings', never with their content's, and they are
/// never empty (§8.3.1: "Margins of elements that establish new block formatting contexts … do not collapse with
/// their in-flow children").</para>
/// </remarks>
internal static class MarginCollapsing
{
    /// <summary>
    /// Combines two adjoining margins. "When two or more margins collapse, the resulting margin width is the
    /// maximum of the collapsing margins' widths" (§8.3.1), which holds because margins are never negative here.
    /// </summary>
    public static double Collapse(double first, double second) => Math.Max(first, second);

    /// <summary>
    /// The top margin of a box as its parent sees it: the box's own top margin collapsed with every margin that
    /// adjoins it from the inside (its first child's top margin, through any empty children before it).
    /// </summary>
    public static double Top(Box box) =>
        IsEmpty(box)
            ? All(box)
            : Collapse(box.Style.Margin.Top.Points, TopAdjoinsChildren(box) ? Leading(box) : 0);

    /// <summary>
    /// The bottom margin of a box as its parent sees it: its own bottom margin collapsed with the last child's
    /// bottom margin (through any empty children after it) when they adjoin.
    /// </summary>
    public static double Bottom(Box box) =>
        IsEmpty(box)
            ? All(box)
            : Collapse(box.Style.Margin.Bottom.Points, BottomAdjoinsChildren(box) ? Trailing(box) : 0);

    /// <summary>
    /// True when the box's top margin adjoins its first child's (§8.3.1, "top margin of a box and top margin of
    /// its first in-flow child"): a flow box without top padding and top border. The caller adds the one exception
    /// the box cannot know itself: a box that establishes a new formatting context, such as the root
    /// (<see cref="ConstraintSpace.IsNewFormattingContext"/>).
    /// </summary>
    public static bool TopAdjoinsChildren(Box box) =>
        box.Layout == LayoutAlgorithmId.Flow && box.Style.Padding.Top.Points == 0 && !box.Style.Border.Top.IsVisible;

    /// <summary>True when the box's bottom margin adjoins its last child's (§8.3.1, "bottom margin of a last in-flow
    /// child and bottom margin of its parent if the parent has 'auto' computed height"): a flow box without
    /// bottom padding and bottom border.</summary>
    public static bool BottomAdjoinsChildren(Box box) =>
        box.Layout == LayoutAlgorithmId.Flow && box.Style.Padding.Bottom.Points == 0 && !box.Style.Border.Bottom.IsVisible;

    /// <summary>
    /// True when the box's own top and bottom margins adjoin (§8.3.1, last case: a box with "zero or 'auto'
    /// computed 'height', and no in-flow children"): no vertical padding or border, and either a flow box whose
    /// children are all empty, or an inline box that makes no line boxes (CSS 2.1 §9.4.2: such line boxes "must be
    /// treated as zero-height line boxes for the purposes of determining the positions of any elements inside of them
    /// … and must be treated as not existing for any other purpose", margin collapsing included).
    /// </summary>
    public static bool IsEmpty(Box box) =>
        box.Style.Padding.Top.Points == 0
        && box.Style.Padding.Bottom.Points == 0
        && !box.Style.Border.Top.IsVisible
        && !box.Style.Border.Bottom.IsVisible
        && (box.Layout == LayoutAlgorithmId.Flow ? box.Children.All(child => child is Box inner && IsEmpty(inner))
            : box.Layout == LayoutAlgorithmId.Inline && !InlineLayout.HasLineBoxes(box));

    /// <summary>Every margin of an empty box and of its (empty) descendants: they all adjoin one another.</summary>
    public static double All(Box box) =>
        box.Children.OfType<Box>().Select(All)
            .Aggregate(Collapse(box.Style.Margin.Top.Points, box.Style.Margin.Bottom.Points), Collapse);

    /// <summary>The margins at the start of a flow box's content: empty children's margins up to and including
    /// the first non-empty child's top margin.</summary>
    private static double Leading(Box box)
    {
        var margin = 0.0;
        foreach (var child in box.Children.OfType<Box>())
        {
            if (!IsEmpty(child))
            {
                return Collapse(margin, Top(child));
            }

            margin = Collapse(margin, All(child));
        }

        return margin;
    }

    /// <summary>The margins at the end of a flow box's content: the last non-empty child's bottom margin and the
    /// margins of the empty children after it.</summary>
    private static double Trailing(Box box)
    {
        var margin = 0.0;
        foreach (var child in box.Children.OfType<Box>().Reverse())
        {
            if (!IsEmpty(child))
            {
                return Collapse(margin, Bottom(child));
            }

            margin = Collapse(margin, All(child));
        }

        return margin;
    }
}

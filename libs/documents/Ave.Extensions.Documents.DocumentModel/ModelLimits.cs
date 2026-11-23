using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.DocumentModel;

/// <summary>
/// The limits every document model is held to, beyond the rules of CSS: the code front-end refuses an author's document
/// beyond them (its <c>InputLimits</c> names these values), and layout checks every model against them before it lays it
/// out (<see cref="ModelCheck"/>), so a model built by hand gets the same answers as one the front-end made.
/// </summary>
public static class ModelLimits
{
    /// <summary>
    /// How deep boxes may nest: at most 64 levels below the root box of the body or of a page area. A table, or a row of
    /// blocks, is one level with its row groups, rows and cells; the content of a cell is the next level. Each level
    /// costs the layout call frames, so without a limit a deep enough document ends the process with a stack overflow.
    /// Deeper nesting is <c>Documents.Input.Invalid.TooDeep</c>.
    /// </summary>
    public const int MaxNestingDepth = 64;

    /// <summary>
    /// The most decorations one run may carry (<see cref="TextStyle.Decorations"/>): 16. Each decorating box around a run adds
    /// one per line it draws, and layout makes one rectangle per decoration for each piece of the run on a line, so without a
    /// limit the work for one run grows with the nesting, not with the size of the text. Sixteen is four nested boxes that each
    /// draw all three lines, with room to spare. More is <c>Documents.Input.Invalid.TextDecoration</c>.
    /// </summary>
    public const int MaxDecorations = 16;

    /// <summary>
    /// The most items one list may have (<see cref="SemanticRole.List"/>): 10 000. Each item's label is shaped and measured
    /// on its own, so the work for one list grows with its number of items; a front-end numbers the items in one pass, and
    /// the limit keeps every number a front-end computes far inside a 32-bit integer from any start. More is
    /// <c>Documents.Input.Invalid.List</c>.
    /// </summary>
    public const int MaxListItems = 10_000;

    /// <summary>
    /// How deep lists may nest, one inside an item of another: 16 levels. Each level adds an indent and a label laid out
    /// outside its item; the general nesting limit (<see cref="MaxNestingDepth"/>, where a list, its item and the item's
    /// body are three levels) would allow 21. Deeper is <c>Documents.Input.Invalid.List</c>.
    /// </summary>
    public const int MaxListDepth = 16;

    /// <summary>
    /// The longest text a list label may hold, all its runs together: 64 UTF-16 code units. The longest label a counter style
    /// of CSS gives a 32-bit number in is 16 ("mmmdccclxxxviii." with its space 17, case <c>li-counter-styles</c>); a label is
    /// shaped once per item and laid out on one line, so its length bounds that work. Longer is
    /// <c>Documents.Input.Invalid.List</c>.
    /// </summary>
    public const int MaxLabelLength = 64;

    /// <summary>
    /// The longest length a document may hold: 14 400 pt (200 inches), the largest page side ISO 32000-1 names (Annex C,
    /// the limit on page size). It holds for a page side, a page margin, a margin, a padding, a border width, a font
    /// size, a fixed column width, a line height given as a length, an image's width and height and a header or footer
    /// height; a line height given as a number is at most this many times the font size. Nothing on a page can usefully
    /// be longer than the largest page, and the limit keeps every coordinate the layout computes far inside the number
    /// range a PDF can hold.
    /// </summary>
    public static Length MaxLength { get; } = Length.FromPoints(14_400);
}

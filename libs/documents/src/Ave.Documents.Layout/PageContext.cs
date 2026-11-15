using System.Globalization;
using Ave.Documents.DocumentModel;

namespace Ave.Documents.Layout;

/// <summary>
/// The page a layout call is laying out, for computed content (<see cref="CounterRun"/>): its number and the number of
/// pages of the document. The page count is the result of the previous layout pass (the paginator's fixpoint).
/// </summary>
/// <param name="Number">The page number, from 1.</param>
/// <param name="Count">The number of pages, at least <paramref name="Number"/> in a settled layout.</param>
public sealed record PageContext(int Number, int Count)
{
    /// <summary>Page 1 of 1: the context before any pagination (for measuring, and for a single-page layout).</summary>
    public static PageContext First { get; } = new(1, 1);

    /// <summary>
    /// The text of a page counter, in the <c>decimal</c> counter style (CSS Counter Styles 3 §6.1: "Western decimal
    /// numbers"), with ASCII digits whatever the culture.
    /// </summary>
    /// <param name="counter">The counter.</param>
    /// <returns>The digits.</returns>
    public string Format(PageCounter counter) =>
        (counter == PageCounter.PageNumber ? Number : Count).ToString(CultureInfo.InvariantCulture);
}

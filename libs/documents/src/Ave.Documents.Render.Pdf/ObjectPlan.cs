namespace Ave.Documents.Render.Pdf;

/// <summary>
/// The object numbers of the file, fixed in advance from the number of fonts and pages, so every object can
/// refer to any other while it is built, and the numbering is the same on every run. Layout of the numbers:
/// catalog, page tree and document information first; then five objects per font; then two per page.
/// </summary>
/// <param name="FontCount">How many fonts the document uses.</param>
/// <param name="PageCount">How many pages it has.</param>
internal sealed record ObjectPlan(int FontCount, int PageCount)
{
    public const int Catalog = 1;
    public const int PageTree = 2;
    public const int Info = 3;
    private const int FirstFontObject = 4;
    private const int ObjectsPerFont = 5;
    private const int ObjectsPerPage = 2;

    /// <summary>The total number of objects; they are numbered 1 to Count.</summary>
    public int Count => Info + FontCount * ObjectsPerFont + PageCount * ObjectsPerPage;

    /// <summary>The numbers of the objects of the font at <paramref name="index"/> (0-based).</summary>
    public FontObjectNumbers Font(int index)
    {
        var first = FirstFontObject + index * ObjectsPerFont;
        return new FontObjectNumbers(Type0: first, CidFont: first + 1, Descriptor: first + 2, FontFile: first + 3, ToUnicode: first + 4);
    }

    /// <summary>The numbers of the objects of the page at <paramref name="index"/> (0-based).</summary>
    public PageObjectNumbers Page(int index)
    {
        var first = FirstFontObject + FontCount * ObjectsPerFont + index * ObjectsPerPage;
        return new PageObjectNumbers(Page: first, Contents: first + 1);
    }
}

/// <summary>The five objects that embed one font as a composite font.</summary>
internal sealed record FontObjectNumbers(int Type0, int CidFont, int Descriptor, int FontFile, int ToUnicode);

/// <summary>The page dictionary and its content stream.</summary>
internal sealed record PageObjectNumbers(int Page, int Contents);

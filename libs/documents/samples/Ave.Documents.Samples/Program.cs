using System;
using System.IO;
using System.Linq;
using Ave.Documents.Fluent;
using Ave.Documents.Fonts;
using Ave.Documents.Layout;
using Ave.Documents.DocumentModel;
using Ave.Documents.PageModel;
using Ave.Documents.Render.Pdf;
using Ave.Extensions.Functional;
using Ave.Documents.Samples;

// Writes the stage-0 samples to the folder given as the first argument (default: ./out), each as a PDF plus the
// three text serialisations of its intermediate models: <name>.model.txt, <name>.fragments.txt, <name>.page.txt.
var output = args.Length > 0 ? args[0] : "out";
Directory.CreateDirectory(output);

var fonts = new FontSet([LoadFont("Lato-Regular.ttf"), LoadFont("Lato-Bold.ttf")]);
var options = new PdfRenderOptions(CreationDate: new DateTimeOffset(2026, 11, 15, 9, 22, 0, TimeSpan.FromHours(1)));

var failures = new (string Name, Document Document)[]
    {
        ("sample-a-letter", SampleDocuments.Letter()),
        ("sample-b-long", SampleDocuments.LongText()),
    }
    .Select(sample => Write(sample.Name, sample.Document))
    .Count(written => !written);
return failures == 0 ? 0 : 1;

bool Write(string name, Document document)
{
    var result = DocumentTranslator.ToModel(document).OnSuccessBind(model =>
        Paginator.Paginate(LayoutEnvironment.Create(fonts), model).OnSuccessBind(fragments =>
        {
            var pages = PagePainter.Paint(model.Metadata, fragments);
            File.WriteAllText(Path.Combine(output, $"{name}.model.txt"), DocumentModelText.Write(model));
            File.WriteAllText(Path.Combine(output, $"{name}.fragments.txt"), FragmentText.Write(fragments));
            File.WriteAllText(Path.Combine(output, $"{name}.page.txt"), PageModelText.Write(pages));
            return PdfRenderer.Render(pages, options).OnSuccessMap(pdf => (Pdf: pdf, PageCount: pages.Pages.Count));
        }));

    return result.Match(
        written =>
        {
            var path = Path.Combine(output, $"{name}.pdf");
            File.WriteAllBytes(path, [.. written.Pdf]);
            Console.WriteLine($"{path}: {written.PageCount} pages, {written.Pdf.Length} bytes");
            return true;
        },
        error =>
        {
            Console.Error.WriteLine($"{name}: {error}");
            return false;
        });
}

static TrueTypeFont LoadFont(string fileName)
{
    var result = FontParser.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fonts", fileName)));
    return result.IsSuccess ? result.Value : throw new InvalidOperationException(result.Error.ToString());
}

/// <summary>Marker so the implicit Program class is documented.</summary>
public partial class Program;

using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// The Chromium reference data is complete: every case in <c>tools/reference/chromium/cases</c> has its seven measured
/// files (and its ink gaps where its print skips ink, its link annotations and their destinations where its print has
/// any, and its images' pixels where it draws an image) and its
/// pin file, <c>measured/&lt;case&gt;.pins.txt</c>: one line <c>file lines</c> per measured file of the case, in ordinal
/// order, with the number of lines the file had when it was measured. So a file that is missing, emptied or cut short
/// fails this test even where no other test reads it (the <c>words</c>, <c>boxes</c> and <c>lines</c> files are kept for
/// comparing a new measurement, the <c>print</c> files are read by the reference tests), and so does a file no pin names.
/// Each case's facts are in files of its own, so two changes that add different cases touch no file in common. After a
/// deliberate new measurement, update the case's pin file with its files. With <c>DOCUMENTS_WRITE_NEW_PINS=1</c> the tests
/// first write the pin file of each case that has none, from its measured files as they are, and add to a pin file that
/// is there a pin for each measured file of its case it does not name (a new kind of measured file); they change no pin
/// that is there.
/// </summary>
public class MeasuredReferenceFilesTests(ITestOutputHelper output)
{
    /// <summary>The kinds of a case's measured files: the seven every case has (with the glyphs of its words,
    /// <c>glyphs.py</c>; the order it paints in, <c>order.py</c>; and the clips around its words, <c>clips.py</c>), then the
    /// gaps of a print that skips ink (<c>ink.py</c>), the link annotations of a print that has any (<c>links.py</c>) with
    /// the left of their destinations (<c>destinations.py</c>), and the pixels of the images of a print that draws any
    /// (<c>images.py</c>).</summary>
    private static readonly string[] Kinds = ["boxes", "clips", "glyphs", "lines", "order", "print", "words", "ink", "links", "destinations", "images"];

    /// <summary>How many of <see cref="Kinds"/> every case has.</summary>
    private const int Always = 7;

    /// <summary>The measured files that are of no case, and their lines: the versions, and Chromium's words of the
    /// generated a2-row-end family (<c>RowEndTests</c>).</summary>
    private static readonly Dictionary<string, int> Shared = new()
    {
        ["a2-row-end-family.tsv"] = 3461,
        ["versions.txt"] = 5,
    };

    private const string PinFile = ".pins.txt";

    /// <summary>The ending of a case's status file, beside its measured files (<c>CaseStatusTests</c> of
    /// <c>UnitTests.Extensions.Documents.Html</c>): not a measured file.</summary>
    private const string StatusFile = ".status.txt";

    private static string Chromium([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "tools", "reference", "chromium"));

    private static string Measured => Path.Combine(Chromium(), "measured");

    private static List<string> Cases() =>
        [.. Directory.GetFiles(Path.Combine(Chromium(), "cases"), "*.html").Select(file => Path.GetFileNameWithoutExtension(file)!).Order(StringComparer.Ordinal)];

    /// <summary>The measured files by name, in ordinal order: every file of the folder but the pin and status files.</summary>
    private static List<string> MeasuredFiles() =>
        [.. Directory.GetFiles(Measured).Select(file => Path.GetFileName(file)!)
            .Where(name => !name.EndsWith(PinFile, StringComparison.Ordinal) && !name.EndsWith(StatusFile, StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

    /// <summary>The cases that have a pin file, by name, in ordinal order.</summary>
    private static List<string> PinnedCases() =>
        [.. Directory.GetFiles(Measured, "*" + PinFile).Select(file => Path.GetFileName(file)[..^PinFile.Length]).Order(StringComparer.Ordinal)];

    /// <summary>The case a measured file is of: its name up to the first dot (no case name has one).</summary>
    private static string CaseOf(string file) => file.Split('.')[0];

    private static int LinesOf(string file) => File.ReadAllText(Path.Combine(Measured, file)).Count(character => character == '\n');

    private static void WriteNewPins()
    {
        if (Environment.GetEnvironmentVariable("DOCUMENTS_WRITE_NEW_PINS") != "1")
        {
            return;
        }

        var files = MeasuredFiles().Where(file => !Shared.ContainsKey(file)).ToList();
        foreach (var name in Cases())
        {
            var path = Path.Combine(Measured, name + PinFile);
            var pins = File.Exists(path) ? File.ReadAllLines(path).Where(line => line.Length > 0).ToList() : [];
            var pinned = pins.Select(line => line.Split(' ')[0]).ToHashSet(StringComparer.Ordinal);
            var added = files.Where(file => CaseOf(file) == name && !pinned.Contains(file)).Select(file => $"{file} {LinesOf(file)}").ToList();
            if (added.Count > 0 || !File.Exists(path))
            {
                File.WriteAllText(path, string.Concat(pins.Concat(added).Order(StringComparer.Ordinal).Select(line => line + "\n")));
            }
        }
    }

    [Fact]
    public void Every_case_has_its_seven_measured_files_and_a_pin_file_and_nothing_else_is_measured()
    {
        WriteNewPins();
        var cases = Cases();
        var files = MeasuredFiles();
        var byCase = files.Where(file => !Shared.ContainsKey(file)).GroupBy(CaseOf).ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var pinned = PinnedCases();
        output.WriteLine($"{cases.Count} cases with a source file, {byCase.Count} with measured files, {pinned.Count} with a pin file; {files.Count} measured files");

        byCase.Keys.Order(StringComparer.Ordinal).Should().Equal(cases, "every case with a source file has measured files, and every measured file but the shared ones is of a case");
        pinned.Should().Equal(cases, "every case has a pin file, and every pin file is of a case");
        files.Where(Shared.ContainsKey).Should().Equal(Shared.Keys.Order(StringComparer.Ordinal), "the versions and Chromium's words of the generated a2-row-end family are measured");
        foreach (var name in cases)
        {
            byCase[name].Should().Contain(Kinds.Take(Always).Select(kind => $"{name}.{kind}.txt"), "every case has its seven measured files")
                .And.BeSubsetOf(Kinds.Select(kind => $"{name}.{kind}.txt"), "a case's measured files are its seven, its ink gaps, its link annotations and destinations, and its images");
        }
    }

    [Fact]
    public void Every_measured_file_has_the_lines_it_was_measured_with()
    {
        WriteNewPins();
        var pins = new List<(string File, int Lines)>();
        foreach (var name in PinnedCases())
        {
            var lines = File.ReadAllLines(Path.Combine(Measured, name + PinFile));
            lines.Should().Equal(lines.Order(StringComparer.Ordinal), $"the pins of {name} are in ordinal order");
            foreach (var fields in lines.Select(line => line.Split(' ')))
            {
                fields.Should().HaveCount(2, $"a pin of {name} is a file and its lines");
                CaseOf(fields[0]).Should().Be(name, $"a pin of {name} names a file of {name}");
                pins.Add((fields[0], int.Parse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture)));
            }
        }

        output.WriteLine($"{pins.Count} pinned files of {PinnedCases().Count} cases, {Shared.Count} shared");
        pins.Select(pin => pin.File).Order(StringComparer.Ordinal).Should().Equal(MeasuredFiles().Where(file => !Shared.ContainsKey(file)), "the pins name every measured file of a case, once, and no other");
        foreach (var (file, count) in pins.Concat(Shared.Select(pin => (pin.Key, pin.Value))))
        {
            LinesOf(file).Should().Be(count, file);
        }
    }
}

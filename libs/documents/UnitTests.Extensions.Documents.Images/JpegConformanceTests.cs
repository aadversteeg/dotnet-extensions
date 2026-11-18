using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using Ave.Extensions.Documents.Images.Jpeg;

namespace UnitTests.Extensions.Documents.Images;

/// <summary>
/// The JPEG decoder against the compliance test data of ITU-T T.83 | ISO/IEC 10918-2 (1994): the streams of the processes
/// the decoder implements (8-bit, Huffman): A1 baseline interleaved, C1 and C2 extended sequential interleaved and not,
/// G1 progressive spectral selection, K1 full progressive (spectral selection and successive approximation), all with
/// restart intervals and four components of mixed sampling factors. The data is free to download but carries no licence
/// grant, so it is not in the repository: it is downloaded on first use into the temporary folder and checked against its
/// SHA-256 (skipped with a message when it cannot be downloaded).
/// </summary>
/// <remarks>
/// Two checks per component, both exact:
/// <list type="bullet">
/// <item>the quantised coefficients the decoder reads from the stream equal the encoder reference data (EREF_*.DCT, the
/// coefficients the streams were made from: entropy decoding, block order, restarts and every progressive scan type);</item>
/// <item>the decoder's samples, transformed back with the reference forward DCT (T.81 §A.3.3, in double precision) and
/// quantised (§A.3.4), equal the decoder reference data (DREF_*.DCT), which T.83 makes the same way from the reference
/// decoder's samples: an inverse DCT accurate enough to give the reference decoder's coefficients back.</item>
/// </list>
/// Both files hold, per component, every block of the component (⌈width/8⌉ × ⌈height/8⌉, edge blocks included) in raster
/// order, 64 big-endian 16-bit coefficients per block in zig-zag order (Readme.txt of the data).
/// </remarks>
[Trait("Category", "Reference")]
public sealed class JpegConformanceTests
{
    private const string Url = "https://www.itu.int/wftp3/Public/t/testsignal/SpeImage/T83/v1994_11/T83_data_files.zip";
    private const string Sha256 = "9f5ed0a74a3087c6c93f4cbb0bd5128c2a87b128e4f59f5b6649e32941da7069";

    private static readonly Lazy<string?> Folder = new(Download);

    /// <summary>Why the data cannot be used here, or null: read when the tests are discovered, which downloads the data
    /// the first time on a machine.</summary>
    public static string? UnavailableReason => Folder.Value is null ? $"Skipped: the T.83 data could not be downloaded from {Url}." : null;

    [ConformanceTheory]
    [InlineData("T83_process1/A1.JPG")]
    [InlineData("T83_process2/C1.JPG")]
    [InlineData("T83_process2/C2.JPG")]
    [InlineData("T83_process6/G1.JPG")]
    [InlineData("T83_process10/K1.JPG")]
    public void A_conformance_stream_decodes_to_the_reference_coefficients(string stream)
    {
        var folder = Folder.Value!;
        var result = JpegDecoder.Components(File.ReadAllBytes(Path.Combine(folder, stream)));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var components = result.Value;
        components.Should().HaveCount(4);

        var names = new[] { "A", "B", "C", "D" };
        for (var index = 0; index < 4; index++)
        {
            var component = components[index];
            var across = (component.Width + 7) / 8;
            var down = (component.Height + 7) / 8;
            var encoder = Read(Path.Combine(folder, "T83_encoder_8", $"EREF_{names[index]}8.DCT"));
            var decoder = Read(Path.Combine(folder, Path.GetDirectoryName(stream)!, $"DREF_{names[index]}8.DCT"));
            encoder.Should().HaveCount(across * down * 64, $"component {names[index]} has {across} x {down} blocks");

            var coefficientDifferences = 0;
            var sampleDifferences = 0;
            for (var row = 0; row < down; row++)
            {
                for (var column = 0; column < across; column++)
                {
                    var block = (row * across) + column;
                    var stored = component.Coefficients.AsSpan(((row * component.BlocksPerLine) + column) * 64, 64);
                    var again = ForwardQuantised(component, row, column);
                    for (var k = 0; k < 64; k++)
                    {
                        coefficientDifferences += stored[k] == encoder[(block * 64) + k] ? 0 : 1;
                        sampleDifferences += again[k] == decoder[(block * 64) + k] ? 0 : 1;
                    }
                }
            }

            coefficientDifferences.Should().Be(0, $"every coefficient of {stream} component {names[index]} equals EREF");
            sampleDifferences.Should().Be(0, $"every re-transformed coefficient of {stream} component {names[index]} equals DREF");
        }
    }

    /// <summary>The reference FDCT (§A.3.3) of one block of the decoder's samples, quantised by rounding (§A.3.4), in
    /// zig-zag order.</summary>
    private static int[] ForwardQuantised(JpegDecoder.DecodedComponent component, int row, int column)
    {
        var stride = component.BlocksPerLine * 8;
        var result = new int[64];
        for (var k = 0; k < 64; k++)
        {
            var position = InverseDct.Natural[k];
            var (v, u) = (position / 8, position % 8);
            var sum = 0.0;
            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 8; x++)
                {
                    var sample = component.Samples[(((row * 8) + y) * stride) + (column * 8) + x] - 128.0;
                    sum += sample * Math.Cos((2 * x + 1) * u * Math.PI / 16) * Math.Cos((2 * y + 1) * v * Math.PI / 16);
                }
            }

            var coefficient = 0.25 * (u == 0 ? 1 / Math.Sqrt(2) : 1) * (v == 0 ? 1 / Math.Sqrt(2) : 1) * sum;
            result[k] = (int)Math.Round(coefficient / component.Quantisation[k], MidpointRounding.AwayFromZero);
        }

        return result;
    }

    private static short[] Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var values = new short[bytes.Length / 2];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(index * 2));
        }

        return values;
    }

    /// <summary>Downloads and unpacks the data once per machine (the folder is named by the SHA-256); null when the
    /// download fails. A file with another digest is an error, not a skip.</summary>
    private static string? Download()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ave-extensions-documents-t83-" + Sha256[..12]);
        if (Directory.Exists(Path.Combine(folder, "T83_process1")))
        {
            return folder;
        }

        byte[] zip;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

            // The ITU server refuses requests without a browser-like user agent.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
            zip = client.GetByteArrayAsync(Url).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Threading.Tasks.TaskCanceledException)
        {
            return null;
        }

        var digest = Convert.ToHexStringLower(SHA256.HashData(zip));
        if (digest != Sha256)
        {
            throw new InvalidOperationException($"The T.83 data downloaded from {Url} has SHA-256 {digest}, not {Sha256}.");
        }

        var unpacking = folder + ".partial-" + Environment.ProcessId;
        ZipFile.ExtractToDirectory(new MemoryStream(zip), unpacking, overwriteFiles: true);
        try
        {
            Directory.Move(unpacking, folder);
        }
        catch (IOException) when (Directory.Exists(folder))
        {
            // Another test process unpacked it first.
            Directory.Delete(unpacking, recursive: true);
        }

        return folder;
    }
}

/// <summary>A theory over the T.83 data: skipped with a message when the data cannot be downloaded.</summary>
public sealed class ConformanceTheoryAttribute : TheoryAttribute
{
    /// <summary>Sets <see cref="FactAttribute.Skip"/> when the data is not available.</summary>
    public ConformanceTheoryAttribute() => Skip = JpegConformanceTests.UnavailableReason;
}

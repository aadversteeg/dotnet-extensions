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
/// grant, so it is not in the repository: the class fixture (<see cref="T83Data"/>) downloads it when a test of this class
/// runs — never when tests are only listed or filtered out — and checks it against its SHA-256. When it cannot be
/// downloaded, the tests fail on CI and are skipped with the reason elsewhere. Trait <c>Category=Network</c>: no Docker is
/// needed, so every CI leg runs them; leave them out with <c>Category!=Network</c>.
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
[Trait("Category", "Network")]
public sealed class JpegConformanceTests(JpegConformanceTests.T83Data data) : IClassFixture<JpegConformanceTests.T83Data>
{
    private const string Url = "https://www.itu.int/wftp3/Public/t/testsignal/SpeImage/T83/v1994_11/T83_data_files.zip";
    private const string Sha256 = "9f5ed0a74a3087c6c93f4cbb0bd5128c2a87b128e4f59f5b6649e32941da7069";

    /// <summary>The user agent the download presents: the project, honestly.</summary>
    private const string UserAgent = "Ave.Extensions.Documents-tests/1.0 (+https://github.com/aadversteeg/dotnet-extensions)";

    [RuntimeSkipTheory]
    [InlineData("T83_process1/A1.JPG")]
    [InlineData("T83_process2/C1.JPG")]
    [InlineData("T83_process2/C2.JPG")]
    [InlineData("T83_process6/G1.JPG")]
    [InlineData("T83_process10/K1.JPG")]
    public void A_conformance_stream_decodes_to_the_reference_coefficients(string stream)
    {
        var folder = data.Folder ?? (data.OnContinuousIntegration
            ? throw new InvalidOperationException(data.Problem)
            : throw new SkipTestException(data.Problem!));
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

    /// <summary>
    /// The T.83 data for the tests of the class: the zip is kept in the temporary folder under its SHA-256 and checked
    /// against it on every use (a file that does not match is fetched again), then unpacked into a folder of its own,
    /// deleted afterwards. Made only when a test of the class runs. A failure is kept as <see cref="Problem"/>, not
    /// thrown, so each test can decide: fail on CI, skip elsewhere.
    /// </summary>
    public sealed class T83Data : IDisposable
    {
        public T83Data()
        {
            OnContinuousIntegration = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" || Environment.GetEnvironmentVariable("CI") == "true";
            try
            {
                var zip = Zip();
                var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-t83-").FullName;
                ZipFile.ExtractToDirectory(new MemoryStream(zip), folder);
                Folder = folder;
            }
            catch (Exception exception) when (exception is HttpRequestException or System.Threading.Tasks.TaskCanceledException or IOException or InvalidDataException)
            {
                Problem = $"The T.83 data could not be fetched from {Url}: {exception.Message}";
            }
        }

        /// <summary>The unpacked data, or null when it could not be fetched.</summary>
        public string? Folder { get; }

        /// <summary>Why there is no data.</summary>
        public string? Problem { get; }

        /// <summary>True on a CI runner, where missing data fails the tests.</summary>
        public bool OnContinuousIntegration { get; }

        public void Dispose()
        {
            if (Folder is not null)
            {
                Directory.Delete(Folder, recursive: true);
            }
        }

        private static byte[] Zip()
        {
            var cache = Path.Combine(Path.GetTempPath(), $"ave-extensions-documents-t83-{Sha256[..12]}.zip");
            if (File.Exists(cache))
            {
                var kept = File.ReadAllBytes(cache);
                if (Convert.ToHexStringLower(SHA256.HashData(kept)) == Sha256)
                {
                    return kept;
                }

                File.Delete(cache);
            }

            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            var zip = client.GetByteArrayAsync(Url).GetAwaiter().GetResult();
            var digest = Convert.ToHexStringLower(SHA256.HashData(zip));
            if (digest != Sha256)
            {
                throw new InvalidDataException($"the file has SHA-256 {digest}, not {Sha256}");
            }

            var partial = cache + ".partial-" + Environment.ProcessId;
            File.WriteAllBytes(partial, zip);
            File.Move(partial, cache, overwrite: true);
            return zip;
        }
    }
}

using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using WebAssembly.Runtime;

namespace UnitTests.Ave.Documents.Typesetting.Reference;

/// <summary>One glyph as HarfBuzz shapes it, in font design units (no scale set, so units are font units).</summary>
public sealed record ReferenceGlyph(int Glyph, int Cluster, int XAdvance, int XOffset, int YOffset);

/// <summary>
/// HarfBuzz 15.0.0 (harfbuzzjs 1.6.4 <c>harfbuzz.wasm</c>) running on the managed WebAssembly runtime of the NuGet
/// package <c>WebAssembly</c>: the reference shaper of the tests. It calls HarfBuzz's C API exactly as a C program
/// would: copy the font and the text into the module's memory, <c>hb_shape</c>, read the glyph info and position
/// arrays back. Test-only; never referenced by the engine.
/// </summary>
public sealed class HarfBuzzReference
{
    // hb_direction_t HB_DIRECTION_LTR; hb_buffer_cluster_level_t HB_BUFFER_CLUSTER_LEVEL_MONOTONE_CHARACTERS
    // (each character keeps its own cluster, as in our shaper); HB_MEMORY_MODE_READONLY.
    private const int DirectionLeftToRight = 4;
    private const int ClusterLevelMonotoneCharacters = 1;
    private const int MemoryModeReadOnly = 1;
    private const int GlyphRecordLength = 20; // hb_glyph_info_t and hb_glyph_position_t are both 5 × 32 bits
    private const int FeatureLength = 16;     // hb_feature_t: tag, value, start, end

    private static readonly Lazy<HarfBuzzReference> Shared = new(() => new HarfBuzzReference());
    private readonly Exports _exports;
    private readonly Dictionary<string, int> _fonts = [];

    /// <summary>The one instance, compiled on first use (WebAssembly to IL, about half a second).</summary>
    public static HarfBuzzReference Instance => Shared.Value;

    private HarfBuzzReference()
    {
        UnmanagedMemory? memory = null;
        var imports = new ImportDictionary
        {
            // The five imports of the Emscripten build. No file system, no clock: shaping needs none of them.
            { "wasi_snapshot_preview1", "proc_exit", new FunctionImport(new Action<int>(code => throw new InvalidOperationException($"HarfBuzz exited with {code}."))) },
            { "env", "_emscripten_runtime_keepalive_clear", new FunctionImport(new Action(() => { })) },
            { "env", "_abort_js", new FunctionImport(new Action(() => throw new InvalidOperationException("HarfBuzz aborted."))) },
            { "env", "_setitimer_js", new FunctionImport(new Func<int, double, int>((_, _) => 0)) },
            {
                "env", "emscripten_resize_heap", new FunctionImport(new Func<int, int>(requested =>
                {
                    var pages = (uint)(((long)(uint)requested + 65535) / 65536);
                    if (pages > memory!.Current)
                    {
                        memory.Grow(pages - memory.Current);
                    }

                    return 1;
                }))
            },
        };

        using var module = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Reference", "harfbuzz.wasm"));
        _exports = Compile.FromBinary<Exports>(module)(imports).Exports;
        memory = _exports.memory;
        Version = ReadString(_exports.hb_version_string());
    }

    /// <summary>The HarfBuzz version reported by the module, for example <c>15.0.0</c>.</summary>
    public string Version { get; }

    /// <summary>
    /// Shapes <paramref name="text"/> left to right in the given script with the given feature settings.
    /// </summary>
    /// <param name="font">The font file.</param>
    /// <param name="text">The text.</param>
    /// <param name="script">The ISO 15924 script, for example <c>Latn</c>.</param>
    /// <param name="features">HarfBuzz feature strings, for example <c>-calt</c>.</param>
    /// <returns>The glyphs in visual (= logical) order.</returns>
    /// <remarks>The module has one linear memory and one allocator, and test classes run in parallel: every call holds
    /// <see cref="_gate"/>, so two shapings never use the module at once.</remarks>
    public ImmutableArray<ReferenceGlyph> Shape(byte[] font, string text, string script, IEnumerable<string> features)
    {
        lock (_gate)
        {
            return ShapeAlone(font, text, script, features);
        }
    }

    private readonly System.Threading.Lock _gate = new();

    private ImmutableArray<ReferenceGlyph> ShapeAlone(byte[] font, string text, string script, IEnumerable<string> features)
    {
        var hbFont = FontFor(font);
        var codePoints = text.EnumerateRunes().Select(rune => rune.Value).ToArray();
        var textPointer = Copy(MemoryMarshal.AsBytes(codePoints.AsSpan()).ToArray());
        var featureList = features.ToArray();
        var featurePointer = _exports.malloc(Math.Max(1, featureList.Length) * FeatureLength);
        for (var index = 0; index < featureList.Length; index++)
        {
            var name = Copy(Encoding.ASCII.GetBytes(featureList[index]));
            _exports.hb_feature_from_string(name, featureList[index].Length, featurePointer + index * FeatureLength);
            _exports.free(name);
        }

        var scriptName = Copy(Encoding.ASCII.GetBytes(script));
        var buffer = _exports.hb_buffer_create();
        _exports.hb_buffer_add_codepoints(buffer, textPointer, codePoints.Length, 0, codePoints.Length);
        _exports.hb_buffer_set_direction(buffer, DirectionLeftToRight);
        _exports.hb_buffer_set_script(buffer, _exports.hb_script_from_string(scriptName, script.Length));
        _exports.hb_buffer_set_cluster_level(buffer, ClusterLevelMonotoneCharacters);
        _exports.hb_shape(hbFont, buffer, featurePointer, featureList.Length);

        var count = _exports.hb_buffer_get_length(buffer);
        var infos = _exports.hb_buffer_get_glyph_infos(buffer, 0);
        var positions = _exports.hb_buffer_get_glyph_positions(buffer, 0);
        var glyphs = Enumerable.Range(0, count).Select(index => new ReferenceGlyph(
            Glyph: Int32(infos + index * GlyphRecordLength),
            Cluster: Int32(infos + index * GlyphRecordLength + 8),
            XAdvance: Int32(positions + index * GlyphRecordLength),
            XOffset: Int32(positions + index * GlyphRecordLength + 8),
            YOffset: Int32(positions + index * GlyphRecordLength + 12))).ToImmutableArray();

        _exports.hb_buffer_destroy(buffer);
        _exports.free(scriptName);
        _exports.free(featurePointer);
        _exports.free(textPointer);
        return glyphs;
    }

    /// <summary>The hb_font_t of a font file, created once: blob → face → font.</summary>
    private int FontFor(byte[] font)
    {
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(font));
        if (!_fonts.TryGetValue(key, out var hbFont))
        {
            var data = Copy(font);
            var blob = _exports.hb_blob_create(data, font.Length, MemoryModeReadOnly, 0, 0);
            hbFont = _exports.hb_font_create(_exports.hb_face_create(blob, 0));
            _fonts[key] = hbFont;
        }

        return hbFont;
    }

    /// <summary>Copies bytes into the module's memory (malloc), returning their address there.</summary>
    private int Copy(byte[] bytes)
    {
        var address = _exports.malloc(Math.Max(1, bytes.Length));
        Marshal.Copy(bytes, 0, _exports.memory.Start + address, bytes.Length);
        return address;
    }

    private int Int32(int address) => Marshal.ReadInt32(_exports.memory.Start + address);

    private string ReadString(int address) => Marshal.PtrToStringUTF8(_exports.memory.Start + address) ?? string.Empty;

    /// <summary>The exports of harfbuzz.wasm used here; the runtime implements them. Names are the C names.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "The names must equal the WebAssembly export names.")]
    public abstract class Exports
    {
        public abstract UnmanagedMemory memory { get; }

        public abstract int malloc(int size);

        public abstract void free(int pointer);

        public abstract int hb_version_string();

        public abstract int hb_blob_create(int data, int length, int mode, int userData, int destroy);

        public abstract int hb_face_create(int blob, int index);

        public abstract int hb_font_create(int face);

        public abstract int hb_buffer_create();

        public abstract void hb_buffer_destroy(int buffer);

        public abstract void hb_buffer_add_codepoints(int buffer, int text, int length, int itemOffset, int itemLength);

        public abstract void hb_buffer_set_direction(int buffer, int direction);

        public abstract void hb_buffer_set_script(int buffer, int script);

        public abstract void hb_buffer_set_cluster_level(int buffer, int level);

        public abstract int hb_script_from_string(int text, int length);

        public abstract int hb_feature_from_string(int text, int length, int feature);

        public abstract void hb_shape(int font, int buffer, int features, int featureCount);

        public abstract int hb_buffer_get_length(int buffer);

        public abstract int hb_buffer_get_glyph_infos(int buffer, int length);

        public abstract int hb_buffer_get_glyph_positions(int buffer, int length);
    }
}

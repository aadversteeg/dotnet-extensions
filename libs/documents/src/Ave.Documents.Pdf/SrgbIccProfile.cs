using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Ave.Documents.Pdf;

/// <summary>
/// A minimal sRGB colour profile, written in code: an ICC version 2 display profile of the matrix/TRC kind (ICC.1:2001-04,
/// the version 2.4 profile format). PDF/A uses it as the destination profile of its output intent, which makes the
/// engine's DeviceRGB and DeviceGray colours device-independent (ISO 19005-3 §6.2.3 and §6.2.4.3).
/// </summary>
/// <remarks>
/// <para>Why written here and not copied from a file: the profile is a few numbers from public standards — the sRGB
/// primaries, white point and transfer function of IEC 61966-2-1 — so it needs no third-party file and no licence. The
/// colorants are the sRGB primaries adapted to the D50 illuminant of the profile connection space; the values are
/// those of the ICC's own <c>sRGB2014.icc</c> and of the HP/Microsoft <c>sRGB IEC61966-2.1</c> profile, which agree to
/// the last bit (decoded from both files, 2026-11-15). The white point is D50, as in <c>sRGB2014.icc</c> (the older HP
/// profile records the unadapted D65 white instead).</para>
/// <para>Why version 2: every PDF/A part and every viewer accepts it; version 4 would add the chromatic adaptation tag
/// and a profile id and gain nothing for a display profile.</para>
/// <para>What it contains (ICC.1:2001-04 §6.3.2.2, Table 25, the required tags of an RGB display profile): description,
/// copyright, media white point, the three colorants (the columns of the RGB → XYZ matrix) and the three tone
/// reproduction curves — one curve, shared by the three tags, as both reference profiles do.</para>
/// </remarks>
public static class SrgbIccProfile
{
    /// <summary>The profile's description, also used as the output intent's condition identifier.</summary>
    public const string Description = "sRGB IEC61966-2.1";

    private const int HeaderLength = 128;
    private const int TagEntryLength = 12;

    /// <summary>Entries of the tone curve: 1024 samples keep the steepest part (near black) accurate to about 1/1000.</summary>
    private const int CurveEntries = 1024;

    /// <summary>The PCS illuminant D50 (ICC.1:2001-04 Annex A.1: "must be set to the CIE Illuminant D50 [X=0.9642,
    /// Y=1.0000, Z=0.8249]"), as s15Fixed16Numbers <c>0000F6D6 00010000 0000D32D</c>.</summary>
    private static readonly (int X, int Y, int Z) D50 = (0xF6D6, 0x10000, 0xD32D);

    /// <summary>The sRGB primaries as XYZ under D50, as s15Fixed16Numbers; each is a column of the matrix that turns
    /// linear RGB into PCS XYZ, and the three add up to D50 within 5/65536, as they must for white. Red is 0.436066,
    /// 0.222488, 0.013916.</summary>
    private static readonly (int X, int Y, int Z) Red = (0x6FA2, 0x38F5, 0x0390);

    /// <summary>Green: 0.385147, 0.716873, 0.097076.</summary>
    private static readonly (int X, int Y, int Z) Green = (0x6299, 0xB785, 0x18DA);

    /// <summary>Blue: 0.143066, 0.060608, 0.714096.</summary>
    private static readonly (int X, int Y, int Z) Blue = (0x24A0, 0x0F84, 0xB6CF);

    /// <summary>The fixed creation date written into the header, so the profile bytes never change.</summary>
    private static readonly DateTime Created = new(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The profile bytes.</summary>
    public static ImmutableArray<byte> Bytes { get; } = Build();

    /// <summary>
    /// The profile file (ICC.1:2001-04 §6, Figure 2): the 128-byte header, the tag table (§6.2, Table 19: a count, then
    /// one 12-byte entry per tag: signature, offset, size), then the tag data. §6.2.2: "All tag data is required to start
    /// on a 4-byte boundary"; §6.2.3: the element size "must not include any padding". The three curve tags point at the
    /// same data.
    /// </summary>
    private static ImmutableArray<byte> Build()
    {
        var curve = CurveType();
        (string Signature, byte[] Data)[] tags =
        [
            ("desc", TextDescriptionType(Description)),
            ("cprt", TextType("No copyright. Written by Ave.Documents from the values of IEC 61966-2-1.")),
            ("wtpt", XyzType(D50)),
            ("rXYZ", XyzType(Red)),
            ("gXYZ", XyzType(Green)),
            ("bXYZ", XyzType(Blue)),
            ("rTRC", curve),
            ("gTRC", curve),
            ("bTRC", curve),
        ];

        var blocks = tags.Select(tag => tag.Data).Distinct().ToList();
        var dataStart = HeaderLength + 4 + tags.Length * TagEntryLength;
        var offsets = new Dictionary<byte[], int>();
        var position = dataStart;
        foreach (var block in blocks)
        {
            offsets[block] = position;
            position += Padded(block.Length);
        }

        var profile = new byte[position];
        var span = profile.AsSpan();
        WriteHeader(span, profile.Length);
        BinaryPrimitives.WriteUInt32BigEndian(span[HeaderLength..], (uint)tags.Length);
        for (var index = 0; index < tags.Length; index++)
        {
            var entry = span[(HeaderLength + 4 + index * TagEntryLength)..];
            WriteSignature(entry, tags[index].Signature);
            BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)offsets[tags[index].Data]);
            BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)tags[index].Data.Length);
        }

        foreach (var block in blocks)
        {
            block.CopyTo(span[offsets[block]..]);
        }

        return [.. profile];
    }

    /// <summary>
    /// The header (ICC.1:2001-04 §6.1, Table 9): size, preferred CMM (none), version 2.1.0, device class <c>mntr</c> (display,
    /// §6.1.4),
    /// colour space <c>RGB </c>, profile connection space <c>XYZ </c>, creation date, the file signature <c>acsp</c>,
    /// platform, flags, manufacturer, model and attributes (all zero: none), rendering intent 0 (perceptual), the PCS
    /// illuminant D50, creator (none); the rest is reserved and zero. PDF/A accepts a display or output profile in RGB
    /// (veraPDF rule 6.2.3-1).
    /// </summary>
    private static void WriteHeader(Span<byte> header, int size)
    {
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)size);
        BinaryPrimitives.WriteUInt32BigEndian(header[8..], 0x02100000);
        WriteSignature(header[12..], "mntr");
        WriteSignature(header[16..], "RGB ");
        WriteSignature(header[20..], "XYZ ");
        ushort[] date = [(ushort)Created.Year, (ushort)Created.Month, (ushort)Created.Day, (ushort)Created.Hour, (ushort)Created.Minute, (ushort)Created.Second];
        for (var index = 0; index < date.Length; index++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(header[(24 + index * 2)..], date[index]);
        }

        WriteSignature(header[36..], "acsp");
        WriteXyzNumber(header[68..], D50);
    }

    /// <summary>XYZType (ICC.1:2001-04 §6.5.26, Table 78): signature <c>XYZ </c>, 4 reserved bytes, then one XYZNumber.</summary>
    private static byte[] XyzType((int X, int Y, int Z) value)
    {
        var data = new byte[20];
        WriteSignature(data, "XYZ ");
        WriteXyzNumber(data.AsSpan(8), value);
        return data;
    }

    /// <summary>
    /// curveType (ICC.1:2001-04 §6.5.3, Table 37): signature <c>curv</c>, 4 reserved bytes, the entry count, then the
    /// entries: "the 16-bit unsigned integers in the range 0 to 65535 linearly map to curve values in the interval [0.0,
    /// 1.0]", sampled evenly over the input 0–1 (1024 entries, as in both reference profiles). The curve is the sRGB decoding function of IEC 61966-2-1
    /// (a non-public standard; the values are those of every public description of sRGB): a linear part below 0.04045, a
    /// 2.4 power above it.
    /// </summary>
    private static byte[] CurveType()
    {
        var data = new byte[12 + CurveEntries * 2];
        WriteSignature(data, "curv");
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), CurveEntries);
        for (var index = 0; index < CurveEntries; index++)
        {
            var linear = SrgbToLinear(index / (double)(CurveEntries - 1));
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12 + index * 2), (ushort)Math.Round(linear * ushort.MaxValue));
        }

        return data;
    }

    /// <summary>The sRGB decoding function (IEC 61966-2-1): from an sRGB component value to a linear light value, both in
    /// 0–1.</summary>
    private static double SrgbToLinear(double value) =>
        value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    /// <summary>
    /// textDescriptionType (ICC.1:2001-04 §6.5.17, Table 68): signature <c>desc</c>, 4 reserved bytes, the ASCII description with
    /// its count (including the terminating zero), then an empty Unicode description (language code and count zero)
    /// and an empty ScriptCode description (code, count, and its fixed 67 bytes), as the clause asks when Unicode and
    /// ScriptCode are "not native on the platform".
    /// </summary>
    private static byte[] TextDescriptionType(string text)
    {
        var ascii = Encoding.ASCII.GetBytes(text + "\0");
        var data = new byte[12 + ascii.Length + 4 + 4 + 2 + 1 + 67];
        WriteSignature(data, "desc");
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), (uint)ascii.Length);
        ascii.CopyTo(data, 12);
        return data;
    }

    /// <summary>textType (ICC.1:2001-04 §6.5.18, Table 70): signature <c>text</c>, 4 reserved bytes, then 7-bit ASCII ending in a zero.</summary>
    private static byte[] TextType(string text) => [.. Signature("text"), 0, 0, 0, 0, .. Encoding.ASCII.GetBytes(text + "\0")];

    /// <summary>An XYZNumber (ICC.1:2001-04 §5.3.10, Table 6): three s15Fixed16Numbers (§5.3.3: "a fixed signed
    /// 4-byte/32-bit quantity which has 16 fractional bits", so 1.0 is <c>00010000h</c>).</summary>
    private static void WriteXyzNumber(Span<byte> target, (int X, int Y, int Z) value)
    {
        BinaryPrimitives.WriteInt32BigEndian(target, value.X);
        BinaryPrimitives.WriteInt32BigEndian(target[4..], value.Y);
        BinaryPrimitives.WriteInt32BigEndian(target[8..], value.Z);
    }

    private static void WriteSignature(Span<byte> target, string signature) => Signature(signature).CopyTo(target);

    private static byte[] Signature(string signature) => Encoding.ASCII.GetBytes(signature);

    private static int Padded(int length) => (length + 3) / 4 * 4;
}

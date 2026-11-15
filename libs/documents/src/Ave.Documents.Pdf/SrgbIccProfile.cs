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
/// numbers are the standard's, adapted to the D50 illuminant of the profile connection space with the Bradford
/// transform (ICC.1:2001-04 Annex E), as every sRGB profile does.</para>
/// <para>Why version 2: every PDF/A part and every viewer accepts it; version 4 would add the chromatic adaptation tag
/// and a profile id and gain nothing for a display profile.</para>
/// <para>What it contains (ICC.1:2001-04 §6.3.1.2, the required tags of a three-component matrix-based display
/// profile): description, copyright, media white point, the three colorants (the columns of the RGB → XYZ matrix) and
/// the three tone reproduction curves (one curve, shared).</para>
/// </remarks>
public static class SrgbIccProfile
{
    /// <summary>The profile's description, also used as the output intent's condition identifier.</summary>
    public const string Description = "sRGB IEC61966-2.1";

    private const int HeaderLength = 128;
    private const int TagEntryLength = 12;

    /// <summary>Entries of the tone curve: 1024 samples keep the steepest part (near black) accurate to about 1/1000.</summary>
    private const int CurveEntries = 1024;

    /// <summary>The PCS illuminant D50 (ICC.1:2001-04 §6.1.14: X = 0.9642, Y = 1.0, Z = 0.8249).</summary>
    private static readonly (double X, double Y, double Z) D50 = (0.9642, 1.0, 0.8249);

    /// <summary>The sRGB primaries as XYZ under D50 (IEC 61966-2-1 primaries, Bradford-adapted from D65); each is a
    /// column of the matrix that turns linear RGB into PCS XYZ. They add up to D50, as they must for white.</summary>
    private static readonly (double X, double Y, double Z) Red = (0.4361, 0.2225, 0.0139);

    private static readonly (double X, double Y, double Z) Green = (0.3851, 0.7169, 0.0971);

    private static readonly (double X, double Y, double Z) Blue = (0.1431, 0.0606, 0.7141);

    /// <summary>The fixed creation date written into the header, so the profile bytes never change.</summary>
    private static readonly DateTime Created = new(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The profile bytes.</summary>
    public static ImmutableArray<byte> Bytes { get; } = Build();

    /// <summary>
    /// The profile file: the 128-byte header, the tag table (a count, then one 12-byte entry per tag: signature,
    /// offset, size), then the tag data, each element starting on a 4-byte boundary (ICC.1:2001-04 §6: "all tagged
    /// element data … shall be padded … to a 4-byte boundary"). The three curve tags point at the same data, which
    /// the format allows ("multiple tags may share the same data").
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
    /// The header (ICC.1:2001-04 §6.1): size, preferred CMM (none), version 2.1.0, device class <c>mntr</c> (display),
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

    /// <summary>XYZType (ICC.1:2001-04 §6.5.26): signature <c>XYZ </c>, 4 reserved bytes, then one XYZNumber.</summary>
    private static byte[] XyzType((double X, double Y, double Z) value)
    {
        var data = new byte[20];
        WriteSignature(data, "XYZ ");
        WriteXyzNumber(data.AsSpan(8), value);
        return data;
    }

    /// <summary>
    /// curveType (ICC.1:2001-04 §6.5.3): signature <c>curv</c>, 4 reserved bytes, the entry count, then the entries as
    /// 16-bit values of the curve sampled evenly over 0–1. The curve is the sRGB decoding function (IEC 61966-2-1
    /// §5.2): a linear part below 0.04045, a 2.4 power above it.
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

    /// <summary>IEC 61966-2-1 §5.2: from an sRGB component value to a linear light value, both in 0–1.</summary>
    private static double SrgbToLinear(double value) =>
        value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    /// <summary>
    /// textDescriptionType (ICC.1:2001-04 §6.5.17): signature <c>desc</c>, 4 reserved bytes, the ASCII description with
    /// its count (including the terminating zero), then an empty Unicode description (language code and count zero)
    /// and an empty ScriptCode description (code, count, and its fixed 67 bytes).
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

    /// <summary>textType (ICC.1:2001-04 §6.5.18): signature <c>text</c>, 4 reserved bytes, then 7-bit ASCII ending in a zero.</summary>
    private static byte[] TextType(string text) => [.. Signature("text"), 0, 0, 0, 0, .. Encoding.ASCII.GetBytes(text + "\0")];

    /// <summary>An XYZNumber (ICC.1:2001-04 §5.3.10): three s15Fixed16Numbers.</summary>
    private static void WriteXyzNumber(Span<byte> target, (double X, double Y, double Z) value)
    {
        BinaryPrimitives.WriteInt32BigEndian(target, S15Fixed16(value.X));
        BinaryPrimitives.WriteInt32BigEndian(target[4..], S15Fixed16(value.Y));
        BinaryPrimitives.WriteInt32BigEndian(target[8..], S15Fixed16(value.Z));
    }

    /// <summary>s15Fixed16Number (ICC.1:2001-04 §5.3.1): a signed number with 16 fraction bits, the value times 65536.</summary>
    private static int S15Fixed16(double value) => (int)Math.Round(value * 65536);

    private static void WriteSignature(Span<byte> target, string signature) => Signature(signature).CopyTo(target);

    private static byte[] Signature(string signature) => Encoding.ASCII.GetBytes(signature);

    private static int Padded(int length) => (length + 3) / 4 * 4;
}

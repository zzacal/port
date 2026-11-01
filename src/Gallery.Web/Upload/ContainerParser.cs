using System.Buffers.Binary;

namespace Gallery.Web.Upload;

/// <summary>Outcome of <see cref="ContainerParser.Parse"/>. <c>Format</c> is "avif" or "webp".</summary>
public sealed record ParseResult(bool Ok, string? Format, int Width, int Height, string? Reason)
{
    internal static ParseResult Fail(string reason) => new(false, null, 0, 0, reason);
}

/// <summary>
/// Reader 1 (research R-05, decisions row 29, plan item 1) [SAFETY]: a hand-written, allowlist-based
/// walk of the AVIF (ISO-BMFF) and WebP (RIFF) containers. Static and pure; no input throws.
/// Every reason is a fixed string naming the box or chunk, never a value read from the file
/// (FR-040). Malformed input gives <c>"malformed {box}"</c>, where {box} is an allowlisted name or
/// the literal "box"/"chunk" when the type is not one we know.
/// </summary>
public static class ContainerParser
{
    // ---- AVIF allowlists (R-05) ----
    private static readonly string[] TopLevelAllowed = ["ftyp", "meta", "mdat", "free", "skip"];
    // meta children: the jSquash set (P4) plus the standard dinf/idat. iref rejects (R-05); anything
    // else is outside the allowlist and rejects.
    private static readonly string[] MetaAllowed = ["hdlr", "pitm", "iloc", "iinf", "iprp", "dinf", "idat"];
    private static readonly string[] IpcoAllowed = ["ispe", "pixi", "av1C", "colr", "pasp"];
    // Known names, used only to make fixed reason strings (never echo an unknown type).
    private static readonly string[] KnownBoxNames =
        ["ftyp", "meta", "mdat", "free", "skip", "hdlr", "pitm", "iloc", "iinf", "iprp", "dinf", "idat",
         "iref", "ipco", "ipma", "infe", "ispe", "pixi", "av1C", "colr", "pasp", "irot", "imir", "clap"];

    // ---- WebP flags (R-05) ----
    /// <summary>Reason used only if an unexpected exception escapes the walk (never expected).</summary>
    public const string InternalErrorReason = "malformed file";

    private const byte FlagIcc = 0x20, FlagExif = 0x08, FlagXmp = 0x04, FlagAnim = 0x02;

    public static ParseResult Parse(ReadOnlySpan<byte> file)
    {
        try
        {
            if (file.Length >= 12 && file[..4].SequenceEqual("RIFF"u8) && file.Slice(8, 4).SequenceEqual("WEBP"u8))
                return ParseWebP(file);
            if (file.Length >= 8 && file.Slice(4, 4).SequenceEqual("ftyp"u8))
                return ParseAvif(file);
            return ParseResult.Fail("unsupported format");
        }
        catch (Exception)
        {
            // Defence in depth only: every length is bounds-checked, so this is not expected to run.
            // A distinct reason lets the fuzz test prove it never does.
            return ParseResult.Fail(InternalErrorReason);
        }
    }

    // =====================================================================================
    // AVIF
    // =====================================================================================

    private readonly record struct BoxHeader(string Type, int Start, int HeaderLen, int End)
    {
        public int PayloadStart => Start + HeaderLen;
        public int PayloadLen => End - PayloadStart;
    }

    private static string Name(string type) => Array.IndexOf(KnownBoxNames, type) is var i and >= 0 ? KnownBoxNames[i] : "box";

    private static bool Is(string type, string[] set) => Array.IndexOf(set, type) >= 0;

    /// <summary>Reads the box header at <paramref name="o"/> inside [.., end). Null on overflow.</summary>
    private static BoxHeader? ReadBox(ReadOnlySpan<byte> b, int o, int end)
    {
        if (end - o < 8) return null;
        ulong size = BinaryPrimitives.ReadUInt32BigEndian(b.Slice(o, 4));
        var type = FourCc(b.Slice(o + 4, 4));
        int header = 8;
        if (size == 1)
        {
            if (end - o < 16) return null;
            size = BinaryPrimitives.ReadUInt64BigEndian(b.Slice(o + 8, 8));
            header = 16;
        }
        else if (size == 0)
        {
            size = (ulong)(end - o);
        }
        if (size < (ulong)header || size > (ulong)(end - o)) return new BoxHeader(type, o, header, -1);
        return new BoxHeader(type, o, header, o + (int)size);
    }

    private static string FourCc(ReadOnlySpan<byte> s)
    {
        Span<char> c = stackalloc char[4];
        for (int i = 0; i < 4; i++) c[i] = (char)s[i];
        return new string(c);
    }

    private static ParseResult ParseAvif(ReadOnlySpan<byte> b)
    {
        int o = 0;
        bool first = true, sawMeta = false;
        int width = 0, height = 0;
        while (o < b.Length)
        {
            var h = ReadBox(b, o, b.Length);
            if (h is null) return ParseResult.Fail("malformed box");
            var box = h.Value;
            if (box.End < 0) return ParseResult.Fail($"malformed {Name(box.Type)}");

            if (first)
            {
                if (box.Type != "ftyp") return ParseResult.Fail("AVIF ftyp not first");
                var ftyp = CheckFtyp(b.Slice(box.PayloadStart, box.PayloadLen));
                if (ftyp is not null) return ParseResult.Fail(ftyp);
                first = false;
            }
            else if (box.Type == "ftyp") return ParseResult.Fail("AVIF second ftyp");
            else if (!Is(box.Type, TopLevelAllowed)) return ParseResult.Fail("AVIF unknown top-level box");
            else if (box.Type == "meta")
            {
                if (sawMeta) return ParseResult.Fail("AVIF second meta");
                sawMeta = true;
                var r = ParseMeta(b, box, out width, out height);
                if (r is not null) return ParseResult.Fail(r);
            }
            o = box.End;
        }
        if (!sawMeta) return ParseResult.Fail("AVIF missing meta");
        return new ParseResult(true, "avif", width, height, null);
    }

    private static string? CheckFtyp(ReadOnlySpan<byte> p)
    {
        // major_brand(4) minor_version(4) compatible_brands(4*n)
        if (p.Length < 8 || (p.Length - 8) % 4 != 0) return "malformed ftyp";
        bool avif = false;
        for (int i = 0; i < p.Length; i += 4)
        {
            if (i == 4) continue; // minor_version
            var brand = p.Slice(i, 4);
            if (brand.SequenceEqual("avis"u8)) return "AVIF brand avis";
            if (brand.SequenceEqual("avif"u8)) avif = true;
        }
        return avif ? null : "AVIF missing avif brand";
    }

    private static string? ParseMeta(ReadOnlySpan<byte> b, BoxHeader meta, out int width, out int height)
    {
        width = height = 0;
        int o = meta.PayloadStart + 4; // FullBox version+flags
        if (o > meta.End) return "malformed meta";
        bool sawIinf = false, sawIpco = false;
        while (o < meta.End)
        {
            var h = ReadBox(b, o, meta.End);
            if (h is null) return "malformed meta";
            var box = h.Value;
            if (box.End < 0) return $"malformed {Name(box.Type)}";
            if (box.Type == "iref") return "AVIF iref box";
            if (!Is(box.Type, MetaAllowed)) return "AVIF unknown meta box";
            if (box.Type == "iinf")
            {
                if (sawIinf) return "AVIF second iinf";
                sawIinf = true;
                var r = ParseIinf(b, box);
                if (r is not null) return r;
            }
            else if (box.Type == "iprp")
            {
                var r = ParseIprp(b, box, ref sawIpco, ref width, ref height);
                if (r is not null) return r;
            }
            o = box.End;
        }
        if (!sawIinf) return "AVIF missing iinf";
        if (!sawIpco) return "AVIF missing ipco";
        if (width <= 0 || height <= 0) return "AVIF missing ispe";
        return null;
    }

    private static string? ParseIinf(ReadOnlySpan<byte> b, BoxHeader iinf)
    {
        int o = iinf.PayloadStart;
        if (iinf.End - o < 4) return "malformed iinf";
        int version = b[o];
        o += 4;
        int countLen = version == 0 ? 2 : 4;
        if (iinf.End - o < countLen) return "malformed iinf";
        long declared = version == 0
            ? BinaryPrimitives.ReadUInt16BigEndian(b.Slice(o, 2))
            : BinaryPrimitives.ReadUInt32BigEndian(b.Slice(o, 4));
        o += countLen;
        int av01 = 0, seen = 0;
        while (o < iinf.End)
        {
            var h = ReadBox(b, o, iinf.End);
            if (h is null) return "malformed iinf";
            var box = h.Value;
            if (box.End < 0) return $"malformed {Name(box.Type)}";
            if (box.Type != "infe") return "AVIF unknown iinf box";
            seen++;
            var p = b.Slice(box.PayloadStart, box.PayloadLen);
            if (p.Length < 4) return "malformed infe";
            int v = p[0];
            if (v < 2) return "AVIF infe version";
            int typeAt = 4 + (v == 2 ? 2 : 4) + 2; // item_ID, item_protection_index
            if (p.Length < typeAt + 4) return "malformed infe";
            var itemType = p.Slice(typeAt, 4);
            if (itemType.SequenceEqual("Exif"u8)) return "AVIF item Exif";
            if (itemType.SequenceEqual("mime"u8)) return "AVIF item mime";
            if (!itemType.SequenceEqual("av01"u8)) return "AVIF unknown item type";
            if (++av01 > 1) return "AVIF second av01 item";
            o = box.End;
        }
        if (seen != declared) return "malformed iinf";
        if (av01 != 1) return "AVIF no av01 item";
        return null;
    }

    private static string? ParseIprp(ReadOnlySpan<byte> b, BoxHeader iprp, ref bool sawIpco, ref int width, ref int height)
    {
        int o = iprp.PayloadStart;
        while (o < iprp.End)
        {
            var h = ReadBox(b, o, iprp.End);
            if (h is null) return "malformed iprp";
            var box = h.Value;
            if (box.End < 0) return $"malformed {Name(box.Type)}";
            if (box.Type == "ipco")
            {
                if (sawIpco) return "AVIF second ipco";
                sawIpco = true;
                var r = ParseIpco(b, box, ref width, ref height);
                if (r is not null) return r;
            }
            else if (box.Type != "ipma") return "AVIF unknown iprp box";
            o = box.End;
        }
        return null;
    }

    private static string? ParseIpco(ReadOnlySpan<byte> b, BoxHeader ipco, ref int width, ref int height)
    {
        int o = ipco.PayloadStart;
        bool sawIspe = false;
        while (o < ipco.End)
        {
            var h = ReadBox(b, o, ipco.End);
            if (h is null) return "malformed ipco";
            var box = h.Value;
            if (box.End < 0) return $"malformed {Name(box.Type)}";
            switch (box.Type)
            {
                case "irot": return "AVIF property irot";
                case "imir": return "AVIF property imir";
                case "clap": return "AVIF property clap";
            }
            if (!Is(box.Type, IpcoAllowed)) return "AVIF unknown property";
            var p = b.Slice(box.PayloadStart, box.PayloadLen);
            if (box.Type == "colr")
            {
                if (p.Length < 4) return "malformed colr";
                if (p[..4].SequenceEqual("prof"u8)) return "AVIF colr prof";
                if (p[..4].SequenceEqual("rICC"u8)) return "AVIF colr rICC";
                if (!p[..4].SequenceEqual("nclx"u8)) return "AVIF colr unknown type";
            }
            else if (box.Type == "ispe")
            {
                if (sawIspe) return "AVIF second ispe";
                sawIspe = true;
                if (p.Length < 12) return "malformed ispe";
                uint w = BinaryPrimitives.ReadUInt32BigEndian(p.Slice(4, 4));
                uint hgt = BinaryPrimitives.ReadUInt32BigEndian(p.Slice(8, 4));
                if (w == 0 || hgt == 0 || w > int.MaxValue || hgt > int.MaxValue) return "malformed ispe";
                width = (int)w;
                height = (int)hgt;
            }
            o = box.End;
        }
        return null;
    }

    // =====================================================================================
    // WebP
    // =====================================================================================

    private static ParseResult ParseWebP(ReadOnlySpan<byte> b)
    {
        uint riffLen = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(4, 4));
        if (riffLen != (uint)(b.Length - 8)) return ParseResult.Fail("malformed RIFF");

        int o = 12;
        bool first = true, sawVp8x = false, sawImage = false;
        int canvasW = 0, canvasH = 0, imageW = 0, imageH = 0;
        while (o < b.Length)
        {
            if (b.Length - o < 8) return ParseResult.Fail("malformed chunk");
            var id = b.Slice(o, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(o + 4, 4));
            long padded = (long)size + (size & 1);
            if (padded > b.Length - o - 8) return ParseResult.Fail($"malformed {ChunkName(id)}");
            var p = b.Slice(o + 8, (int)size);

            if (id.SequenceEqual("ICCP"u8)) return ParseResult.Fail("WebP ICCP chunk");
            if (id.SequenceEqual("EXIF"u8)) return ParseResult.Fail("WebP EXIF chunk");
            if (id.SequenceEqual("XMP "u8)) return ParseResult.Fail("WebP XMP chunk");
            if (id.SequenceEqual("ANIM"u8)) return ParseResult.Fail("WebP ANIM chunk");
            if (id.SequenceEqual("ANMF"u8)) return ParseResult.Fail("WebP ANMF chunk");

            if (id.SequenceEqual("VP8X"u8))
            {
                if (!first || sawVp8x) return ParseResult.Fail("WebP VP8X not first");
                sawVp8x = true;
                if (p.Length < 10) return ParseResult.Fail("malformed VP8X");
                byte flags = p[0];
                if ((flags & FlagIcc) != 0) return ParseResult.Fail("VP8X ICC flag");
                if ((flags & FlagExif) != 0) return ParseResult.Fail("VP8X EXIF flag");
                if ((flags & FlagXmp) != 0) return ParseResult.Fail("VP8X XMP flag");
                if ((flags & FlagAnim) != 0) return ParseResult.Fail("VP8X animation flag");
                canvasW = (p[4] | p[5] << 8 | p[6] << 16) + 1;
                canvasH = (p[7] | p[8] << 8 | p[9] << 16) + 1;
            }
            else if (id.SequenceEqual("VP8 "u8))
            {
                if (sawImage) return ParseResult.Fail("WebP second image chunk");
                sawImage = true;
                // frame tag (3) + start code 9d 01 2a + 14-bit width + 14-bit height
                if (p.Length < 10 || p[3] != 0x9d || p[4] != 0x01 || p[5] != 0x2a) return ParseResult.Fail("malformed VP8");
                if ((p[0] & 0x01) != 0) return ParseResult.Fail("malformed VP8"); // must be a key frame
                imageW = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(6, 2)) & 0x3fff;
                imageH = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(8, 2)) & 0x3fff;
            }
            else if (id.SequenceEqual("VP8L"u8))
            {
                if (sawImage) return ParseResult.Fail("WebP second image chunk");
                sawImage = true;
                if (p.Length < 5 || p[0] != 0x2f) return ParseResult.Fail("malformed VP8L");
                uint bits = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(1, 4));
                imageW = (int)(bits & 0x3fff) + 1;
                imageH = (int)((bits >> 14) & 0x3fff) + 1;
            }
            else if (id.SequenceEqual("ALPH"u8))
            {
                if (!sawVp8x) return ParseResult.Fail("WebP ALPH without VP8X");
            }
            else
            {
                return ParseResult.Fail("WebP unknown chunk");
            }

            first = false;
            o += 8 + (int)padded;
        }
        if (!sawImage) return ParseResult.Fail("WebP missing image chunk");
        if (imageW <= 0 || imageH <= 0) return ParseResult.Fail("WebP empty dimension");
        if (sawVp8x && (canvasW != imageW || canvasH != imageH)) return ParseResult.Fail("WebP canvas mismatch");
        return new ParseResult(true, "webp", imageW, imageH, null);
    }

    private static string ChunkName(ReadOnlySpan<byte> id)
    {
        if (id.SequenceEqual("VP8 "u8)) return "VP8";
        if (id.SequenceEqual("VP8L"u8)) return "VP8L";
        if (id.SequenceEqual("VP8X"u8)) return "VP8X";
        if (id.SequenceEqual("ALPH"u8)) return "ALPH";
        return "chunk";
    }
}

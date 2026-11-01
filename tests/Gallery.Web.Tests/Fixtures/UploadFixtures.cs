using System.Buffers.Binary;
using System.Text;

namespace Gallery.Web.Tests.Fixtures;

/// <summary>
/// T018: fixture bytes for the 008 readers, plus minimal ISO-BMFF and RIFF writers that build hostile
/// variants of the real jSquash output in code (tasks.md T003: structural cases are not files).
/// Paths go through <see cref="Render.RepoRoot"/>, never a fixed count of "..".
/// </summary>
public static class UploadFixtures
{
    private static readonly string UploadDir = Path.Combine(Render.RepoRoot, "tests", "fixtures", "upload");
    private static readonly string FixtureDir = Path.Combine(Render.RepoRoot, "tests", "fixtures");

    /// <summary>Loads <c>tests/fixtures/upload/{name}</c>, else <c>tests/fixtures/{name}</c>.</summary>
    public static byte[] Bytes(string name)
    {
        var upload = Path.Combine(UploadDir, name);
        if (File.Exists(upload)) return File.ReadAllBytes(upload);
        var plain = Path.Combine(FixtureDir, name);
        if (File.Exists(plain)) return File.ReadAllBytes(plain);
        throw new FileNotFoundException($"Fixture not found: {name} (looked in {UploadDir} and {FixtureDir})");
    }

    // =====================================================================================
    // ISO-BMFF writer
    // =====================================================================================

    public static byte[] Box(string type, params byte[][] payload)
    {
        var body = Concat(payload);
        var b = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(b, (uint)b.Length);
        Encoding.ASCII.GetBytes(type, b.AsSpan(4, 4));
        body.CopyTo(b, 8);
        return b;
    }

    public static byte[] FullBox(string type, byte version, uint flags, params byte[][] payload)
    {
        var vf = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(vf, ((uint)version << 24) | (flags & 0xFFFFFF));
        return Box(type, [vf, .. payload]);
    }

    /// <summary>An <c>infe</c> version 2 entry: item_ID, protection index 0, item_type, empty name.</summary>
    public static byte[] Infe(ushort itemId, string itemType) =>
        FullBox("infe", 2, 0, U16(itemId), U16(0), Ascii(itemType), [0]);

    /// <summary>A parsed box. Containers keep their children; leaves keep their raw payload.</summary>
    public sealed class Node
    {
        public required string Type { get; set; }
        public byte[] Payload { get; set; } = [];
        /// <summary>Bytes between the header and the children (FullBox header, iinf count).</summary>
        public byte[] Prefix { get; set; } = [];
        public List<Node>? Children { get; set; }

        public byte[] ToBytes()
        {
            if (Children is null) return Box(Type, Payload);
            if (Type == "iinf")
            {
                // Rewrite entry_count for the current children (version 0: 16-bit, else 32-bit).
                var version = Prefix[0];
                Prefix = version == 0
                    ? [.. Prefix[..4], .. U16((ushort)Children.Count)]
                    : [.. Prefix[..4], .. U32((uint)Children.Count)];
            }
            return Box(Type, [Prefix, .. Children.Select(c => c.ToBytes())]);
        }

        public Node Find(string type) =>
            FindOrNull(type) ?? throw new InvalidOperationException($"box {type} not found");

        public Node? FindOrNull(string type)
        {
            if (Children is null) return null;
            foreach (var c in Children)
            {
                if (c.Type == type) return c;
                if (c.FindOrNull(type) is { } deeper) return deeper;
            }
            return null;
        }
    }

    /// <summary>Parses a well-formed AVIF (32-bit sizes) into a box tree.</summary>
    public static Node ParseAvif(byte[] file) =>
        new() { Type = "<root>", Children = ParseBoxes(file, 0, file.Length) };

    public static byte[] Serialize(Node root) => Concat([.. root.Children!.Select(c => c.ToBytes())]);

    private static List<Node> ParseBoxes(byte[] b, int start, int end)
    {
        var list = new List<Node>();
        int o = start;
        while (o < end)
        {
            int size = (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o, 4));
            if (size < 8) throw new InvalidOperationException("fixture helper needs 32-bit box sizes");
            var type = Encoding.ASCII.GetString(b, o + 4, 4);
            int p = o + 8, e = o + size;
            int prefixLen = type switch
            {
                "meta" => 4,
                "iinf" => 4 + (b[p] == 0 ? 2 : 4),
                "iprp" or "ipco" => 0,
                _ => -1,
            };
            if (prefixLen < 0)
                list.Add(new Node { Type = type, Payload = b[p..e] });
            else
                list.Add(new Node { Type = type, Prefix = b[p..(p + prefixLen)], Children = ParseBoxes(b, p + prefixLen, e) });
            o = e;
        }
        return list;
    }

    private static byte[] MutateAvif(byte[] avif, Action<Node> mutate)
    {
        var root = ParseAvif(avif);
        mutate(root);
        return Serialize(root);
    }

    // ---- AVIF mutations (each over a real jSquash file, default orient6-200.avif) ----

    public static byte[] Orient6Avif => Bytes("orient6-200.avif");

    /// <summary>A second <c>infe(av01)</c>: an embedded thumbnail.</summary>
    public static byte[] AvifWithSecondAv01(byte[]? avif = null) => AvifWithItem("av01", avif);

    /// <summary>An extra <c>infe</c> of the given item type (<c>Exif</c>, <c>mime</c>, <c>av01</c>).</summary>
    public static byte[] AvifWithItem(string itemType, byte[]? avif = null) =>
        MutateAvif(avif ?? Orient6Avif, root => root.Find("iinf").Children!.Add(new Node
        {
            Type = "infe",
            Payload = Infe(2, itemType)[8..],
        }));

    /// <summary>An <c>iref</c> box in <c>meta</c> (a thumbnail/cdsc link from item 2 to item 1).</summary>
    public static byte[] AvifWithIref(byte[]? avif = null) =>
        MutateAvif(avif ?? Orient6Avif, root =>
        {
            var iref = FullBox("iref", 0, 0, Box("thmb", U16(2), U16(1), U16(1)));
            root.Find("meta").Children!.Add(new Node { Type = "iref", Payload = iref[8..] });
        });

    /// <summary>An extra <c>ipco</c> property: <c>irot</c>, <c>imir</c> or <c>clap</c>.</summary>
    public static byte[] AvifWithProperty(string type, byte[]? avif = null)
    {
        byte[] payload = type switch
        {
            "irot" => [1],
            "imir" => [0],
            "clap" => new byte[32],
            _ => [],
        };
        return MutateAvif(avif ?? Orient6Avif, root =>
            root.Find("ipco").Children!.Add(new Node { Type = type, Payload = payload }));
    }

    /// <summary><c>colr</c> type changed from <c>nclx</c> to <c>prof</c> (an ICC profile).</summary>
    public static byte[] AvifWithColrProf(byte[]? avif = null) =>
        MutateAvif(avif ?? Orient6Avif, root =>
        {
            var colr = root.Find("colr");
            colr.Payload = [.. Ascii("prof"), .. new byte[16]];
        });

    /// <summary>The <c>ftyp</c> major brand changed to <c>avis</c> (an image sequence).</summary>
    public static byte[] AvifWithBrandAvis(byte[]? avif = null) =>
        MutateAvif(avif ?? Orient6Avif, root =>
        {
            var ftyp = root.Find("ftyp");
            var p = ftyp.Payload.ToArray();
            Encoding.ASCII.GetBytes("avis", p.AsSpan(0, 4));
            ftyp.Payload = p;
        });

    /// <summary>An unknown top-level box appended after <c>mdat</c>.</summary>
    public static byte[] AvifWithUnknownTopLevelBox(byte[]? avif = null) =>
        MutateAvif(avif ?? Orient6Avif, root =>
            root.Children!.Add(new Node { Type = "zzzz", Payload = [1, 2, 3, 4] }));

    /// <summary>The file cut short by <paramref name="cut"/> bytes (inside <c>mdat</c> by default).</summary>
    public static byte[] Truncated(byte[] file, int cut = 10) => file[..(file.Length - cut)];

    /// <summary>
    /// The last top-level box (<c>mdat</c> in jSquash output) rewritten with size 1 + 64-bit largesize
    /// (as the sips AVIF does, probe P4). Offsets in <c>iloc</c> are not adjusted; reader 1 does not
    /// follow them.
    /// </summary>
    public static byte[] AvifWithLargesizeMdat(byte[]? avif = null)
    {
        var src = avif ?? Orient6Avif;
        var (start, size) = LastTopLevelBox(src);
        var b = new byte[src.Length + 8];
        src.AsSpan(0, start).CopyTo(b);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(start, 4), 1);
        src.AsSpan(start + 4, 4).CopyTo(b.AsSpan(start + 4));
        BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(start + 8, 8), (ulong)size + 8);
        src.AsSpan(start + 8).CopyTo(b.AsSpan(start + 16));
        return b;
    }

    /// <summary>The last top-level box rewritten with size 0 ("extends to end of file").</summary>
    public static byte[] AvifWithSizeZeroMdat(byte[]? avif = null)
    {
        var b = (avif ?? Orient6Avif).ToArray();
        var (start, _) = LastTopLevelBox(b);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(start, 4), 0);
        return b;
    }

    private static (int Start, int Size) LastTopLevelBox(byte[] b)
    {
        int o = 0, last = 0, size = 0;
        while (o < b.Length)
        {
            size = (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o, 4));
            last = o;
            o += size;
        }
        return (last, size);
    }

    // =====================================================================================
    // RIFF writer
    // =====================================================================================

    public static byte[] Chunk(string id, byte[] payload)
    {
        var pad = payload.Length % 2;
        var b = new byte[8 + payload.Length + pad];
        Encoding.ASCII.GetBytes(id, b.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4, 4), (uint)payload.Length);
        payload.CopyTo(b, 8);
        return b;
    }

    /// <summary>RIFF/WEBP around the given chunks. <paramref name="lengthDelta"/> corrupts the RIFF length.</summary>
    public static byte[] Riff(IEnumerable<byte[]> chunks, int lengthDelta = 0)
    {
        var body = Concat([Ascii("WEBP"), .. chunks]);
        var b = new byte[8 + body.Length];
        Encoding.ASCII.GetBytes("RIFF", b.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4, 4), (uint)(body.Length + lengthDelta));
        body.CopyTo(b, 8);
        return b;
    }

    /// <summary>The chunks of a WebP file, each including its 8-byte header and pad byte.</summary>
    public static List<(string Id, byte[] Bytes)> Chunks(byte[] webp)
    {
        var list = new List<(string, byte[])>();
        int o = 12;
        while (o + 8 <= webp.Length)
        {
            var id = Encoding.ASCII.GetString(webp, o, 4);
            int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(o + 4, 4));
            int total = 8 + len + (len % 2);
            list.Add((id, webp[o..(o + total)]));
            o += total;
        }
        return list;
    }

    /// <summary>A VP8X chunk with the given flags and canvas size.</summary>
    public static byte[] Vp8x(byte flags, int width, int height)
    {
        var p = new byte[10];
        p[0] = flags;
        WriteU24(p.AsSpan(4), width - 1);
        WriteU24(p.AsSpan(7), height - 1);
        return Chunk("VP8X", p);
    }

    public const byte FlagIcc = 0x20, FlagAlpha = 0x10, FlagExif = 0x08, FlagXmp = 0x04, FlagAnim = 0x02;

    public static byte[] Orient6Webp => Bytes("orient6-200.webp");

    /// <summary>The image chunk(s) of a simple-format WebP (VP8 / VP8L).</summary>
    private static byte[][] ImageChunks(byte[] webp) => [.. Chunks(webp).Select(c => c.Bytes)];

    /// <summary>A WebP with an extra chunk (e.g. <c>ICCP</c>, <c>EXIF</c>, <c>XMP </c>, <c>ANIM</c>, unknown).</summary>
    public static byte[] WebpWithChunk(string id, byte[]? payload = null, byte[]? webp = null) =>
        Riff([.. ImageChunks(webp ?? Orient6Webp), Chunk(id, payload ?? [1, 2, 3, 4])]);

    /// <summary>A VP8X-format WebP with the given flags set and nothing else added.</summary>
    public static byte[] WebpWithVp8xFlags(byte flags, byte[]? webp = null)
    {
        var src = webp ?? Orient6Webp;
        var (w, h) = Vp8Dimensions(src);
        return Riff([Vp8x(flags, w, h), .. ImageChunks(src)]);
    }

    /// <summary>
    /// The camera-plus-GPS case: VP8X with the EXIF flag, the image, and an EXIF chunk holding the
    /// real TIFF block of <c>tests/fixtures/gps-camera.jpg</c> (so reader 2 also sees GPS).
    /// </summary>
    public static byte[] WebpWithGpsExif(byte[]? webp = null)
    {
        var src = webp ?? Bytes("gps-320.webp");
        var (w, h) = Vp8Dimensions(src);
        return Riff([Vp8x(FlagExif, w, h), .. ImageChunks(src), Chunk("EXIF", GpsTiffBlock())]);
    }

    /// <summary>The fixture with a wrong RIFF length field.</summary>
    public static byte[] WebpWithWrongRiffLength(int delta = 2, byte[]? webp = null) =>
        Riff(ImageChunks(webp ?? Orient6Webp), delta);

    /// <summary>A copy of a simple-format (VP8) WebP whose frame header says a different size.</summary>
    public static byte[] WebpWithVp8Dimensions(byte[] webp, int width, int height)
    {
        var copy = webp.ToArray();
        var at = FindVp8(copy) + 8 + 6;
        BinaryPrimitives.WriteUInt16LittleEndian(copy.AsSpan(at, 2), (ushort)(width & 0x3fff));
        BinaryPrimitives.WriteUInt16LittleEndian(copy.AsSpan(at + 2, 2), (ushort)(height & 0x3fff));
        return copy;
    }

    public static (int W, int H) Vp8Dimensions(byte[] webp)
    {
        var at = FindVp8(webp) + 8 + 6;
        return (BinaryPrimitives.ReadUInt16LittleEndian(webp.AsSpan(at, 2)) & 0x3fff,
                BinaryPrimitives.ReadUInt16LittleEndian(webp.AsSpan(at + 2, 2)) & 0x3fff);
    }

    private static int FindVp8(byte[] webp)
    {
        int o = 12;
        while (o + 8 <= webp.Length)
        {
            if (Encoding.ASCII.GetString(webp, o, 4) == "VP8 ") return o;
            int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(o + 4, 4));
            o += 8 + len + (len % 2);
        }
        throw new InvalidOperationException("no VP8 chunk in fixture");
    }

    /// <summary>The TIFF block (after "Exif\0\0") of the APP1 segment in gps-camera.jpg.</summary>
    public static byte[] GpsTiffBlock()
    {
        var jpg = Bytes("gps-camera.jpg");
        int o = 2;
        while (o + 4 <= jpg.Length && jpg[o] == 0xFF)
        {
            int len = BinaryPrimitives.ReadUInt16BigEndian(jpg.AsSpan(o + 2, 2));
            if (jpg[o + 1] == 0xE1 && Encoding.ASCII.GetString(jpg, o + 4, 4) == "Exif")
                return jpg[(o + 10)..(o + 2 + len)];
            o += 2 + len;
        }
        throw new InvalidOperationException("no Exif APP1 in gps-camera.jpg");
    }

    // ---- helpers ----

    public static string DataUri(byte[] webp) => "data:image/webp;base64," + Convert.ToBase64String(webp);

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    private static byte[] U16(ushort v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, v); return b; }

    private static byte[] U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); return b; }

    private static void WriteU24(Span<byte> s, int v) { s[0] = (byte)v; s[1] = (byte)(v >> 8); s[2] = (byte)(v >> 16); }

    private static byte[] Concat(byte[][] parts)
    {
        var all = new byte[parts.Sum(p => p.Length)];
        int o = 0;
        foreach (var p in parts) { p.CopyTo(all, o); o += p.Length; }
        return all;
    }
}

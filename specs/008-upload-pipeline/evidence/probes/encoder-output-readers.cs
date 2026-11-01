#:package MetadataExtractor@2.9.3
#:property PublishAot=false
// Probe 008-P4: what MetadataExtractor reports for jSquash output (colour profile risk, decisions row 9),
// plus a raw AVIF box walk listing colr type and iinf item types.
using MetadataExtractor;
using System.Text;

foreach (var f in System.IO.Directory.GetFiles(args[0]).OrderBy(x => x))
{
    var dirs = ImageMetadataReader.ReadMetadata(f);
    var names = string.Join("|", dirs.Select(d => d.Name).Distinct());
    var extra = "";
    if (f.EndsWith(".avif")) extra = AvifBoxes(File.ReadAllBytes(f));
    if (f.EndsWith(".webp")) extra = WebpChunks(File.ReadAllBytes(f));
    Console.WriteLine($"{Path.GetFileName(f)}: dirs=[{names}] {extra}");
}

static uint BE(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
static string CC(byte[] b, int o) => Encoding.ASCII.GetString(b, o, 4);

static string AvifBoxes(byte[] b)
{
    var sb = new StringBuilder("boxes=");
    Walk(b, 0, b.Length, sb, 0);
    return sb.ToString();
}
static void Walk(byte[] b, int start, int end, StringBuilder sb, int depth)
{
    int o = start;
    while (o + 8 <= end)
    {
        int len = (int)BE(b, o); var t = CC(b, o + 4);
        if (len < 8 || o + len > end) { sb.Append($"!bad@{o}"); return; }
        sb.Append(t);
        if (t == "colr") sb.Append("(" + CC(b, o + 8) + ")");
        if (t == "ftyp") sb.Append("(" + CC(b, o + 8) + ")");
        if (t == "ispe") sb.Append($"({BE(b, o + 12)}x{BE(b, o + 16)})");
        if (t == "infe") { int v = b[o + 8]; int tp = o + 12 + (v >= 3 ? 4 : 2) + 2; sb.Append("(" + CC(b, tp) + ")"); }
        var children = t switch { "meta" => o + 12, "iprp" => o + 8, "ipco" => o + 8, "iinf" => o + 12 + (b[o + 8] == 0 ? 2 : 4), _ => -1 };
        if (children > 0) { sb.Append('{'); Walk(b, children, o + len, sb, depth + 1); sb.Append('}'); }
        sb.Append(' ');
        o += len;
    }
}
static string WebpChunks(byte[] b)
{
    var sb = new StringBuilder("chunks=");
    int o = 12;
    while (o + 8 <= b.Length)
    {
        var id = CC(b, o); int len = BitConverter.ToInt32(b, o + 4);
        sb.Append(id.Trim());
        if (id == "VP8X") sb.Append($"(flags=0x{b[o + 8]:x2})");
        sb.Append(' ');
        o += 8 + len + (len % 2);
    }
    return sb.ToString();
}

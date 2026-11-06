using System.Text.RegularExpressions;

namespace Gallery.Web.Tests;

/// <summary>
/// 013 T008 [SAFETY] II.2 (research R-09, single writer): only <c>Library/SiteProjector.cs</c>
/// writes the derived <c>cameras</c> collection. A source scan modelled on
/// <see cref="NavSourceGuardTests"/>: in every <c>src/**/*.cs</c> outside <c>bin</c>/<c>obj</c>,
/// (1) the literal <c>"cameras"</c> may appear only in the projector, the reader, the indexes and
/// the documents; (2) a collection of <c>CameraGroupDoc</c> may be written (<c>InsertOne</c>,
/// <c>ReplaceOne</c>, <c>UpdateOne</c>, <c>UpdateMany</c>, <c>DeleteOne</c>, <c>DeleteMany</c>)
/// only in the projector. Do not widen the allow-lists to make a new writer pass.
/// </summary>
public class CameraSourceGuardTests
{
    private static readonly string[] LiteralAllowed =
    [
        "Gallery.Web/Library/SiteProjector.cs",
        "Gallery.Web/Data/ContentReader.cs",
        "Gallery.Web/Data/Indexes.cs",
        "Gallery.Web/Data/Documents.cs",
    ];

    private const string Writer = "Gallery.Web/Library/SiteProjector.cs";

    private static readonly Regex CamerasLiteral = new("\"cameras\"", RegexOptions.CultureInvariant);
    private static readonly Regex CameraCollection = new(@"IMongoCollection<\s*CameraGroupDoc\s*>\s+(\w+)", RegexOptions.CultureInvariant);
    private static readonly Regex InlineCameraCollection = new(@"GetCollection<\s*CameraGroupDoc\s*>\([^)]*\)\s*\.\s*(InsertOne|ReplaceOne|UpdateOne|UpdateMany|DeleteOne|DeleteMany)", RegexOptions.CultureInvariant);
    private const string WriteOps = @"(InsertOne|ReplaceOne|UpdateOne|UpdateMany|DeleteOne|DeleteMany)";

    /// <summary>The <c>"cameras"</c> literals outside the allow-list, as <c>path:line: text</c>.</summary>
    internal static IEnumerable<string> LiteralViolations(string rel, IReadOnlyList<string> lines)
    {
        if (LiteralAllowed.Contains(rel)) yield break;
        for (var i = 0; i < lines.Count; i++)
            if (CamerasLiteral.IsMatch(lines[i]))
                yield return $"{rel}:{i + 1}: {lines[i].Trim()}";
    }

    /// <summary>
    /// The writes on a <c>CameraGroupDoc</c> collection outside the projector: a write op called on a
    /// variable/field/property declared as <c>IMongoCollection&lt;CameraGroupDoc&gt;</c> in the
    /// same file, or on an inline <c>GetCollection&lt;CameraGroupDoc&gt;(…)</c>.
    /// </summary>
    internal static IEnumerable<string> WriteViolations(string rel, IReadOnlyList<string> lines)
    {
        if (rel == Writer) yield break;
        var names = lines.SelectMany(l => CameraCollection.Matches(l).Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal).ToList();
        var onName = names.Count == 0
            ? null
            : new Regex($@"\b({string.Join("|", names.Select(Regex.Escape))})\s*\.\s*{WriteOps}", RegexOptions.CultureInvariant);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (InlineCameraCollection.IsMatch(line) || (onName?.IsMatch(line) ?? false))
                yield return $"{rel}:{i + 1}: {line.Trim()}";
        }
    }

    private static List<(string Rel, string[] Lines)> Sources()
    {
        var src = Path.Combine(Render.RepoRoot, "src");
        var files = new List<(string, string[])>();
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file).Replace('\\', '/');
            if (rel.Contains("/bin/") || rel.Contains("/obj/")) continue;
            files.Add((rel, File.ReadAllLines(file)));
        }
        return files;
    }

    [Fact]
    public void The_cameras_literal_appears_only_in_the_allowed_files()
    {
        var sources = Sources();
        Assert.True(sources.Count > 0);
        var violations = sources.SelectMany(s => LiteralViolations(s.Rel, s.Lines)).ToList();
        Assert.True(violations.Count == 0,
            "\"cameras\" named outside the allowed files (II.2 single writer):\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Only_the_projector_writes_a_CameraGroupDoc_collection()
    {
        var violations = Sources().SelectMany(s => WriteViolations(s.Rel, s.Lines)).ToList();
        Assert.True(violations.Count == 0,
            "cameras written outside SiteProjector.cs (II.2 single writer):\n" + string.Join("\n", violations));
    }

    [Fact]
    public void The_projector_itself_replaces_camera_documents_so_the_scan_is_not_vacuous()
    {
        var projector = File.ReadAllLines(Path.Combine(Render.RepoRoot, "src", "Gallery.Web", "Library", "SiteProjector.cs"));
        Assert.Contains(projector, l => l.Contains("ReplaceOneAsync", StringComparison.Ordinal));
        Assert.Contains(projector, l => CamerasLiteral.IsMatch(l));
        Assert.Contains(projector, l => CameraCollection.IsMatch(l));
        // The projector's own write lines would be reported anywhere else.
        Assert.NotEmpty(WriteViolations("Gallery.Web/Library/ImageService.cs", projector));
    }

    [Theory]
    [InlineData("var c = db.GetCollection<BsonDocument>(\"cameras\");")]
    [InlineData("    await _db.GetCollection<CameraGroupDoc>(\"cameras\").DeleteManyAsync(f);")]
    public void A_cameras_literal_elsewhere_is_reported_with_its_path_and_line(string line)
    {
        var found = LiteralViolations("Gallery.Web/Library/ImageService.cs", ["// header", line]).ToList();
        Assert.Equal(new[] { $"Gallery.Web/Library/ImageService.cs:2: {line.Trim()}" }, found.ToArray());
    }

    [Theory]
    [InlineData("await _cams.InsertOneAsync(doc);")]
    [InlineData("await _cams.ReplaceOneAsync(f, doc);")]
    [InlineData("await _cams.UpdateOneAsync(f, u);")]
    [InlineData("await _cams.UpdateManyAsync(f, u);")]
    [InlineData("await _cams.DeleteOneAsync(f);")]
    [InlineData("await _cams.DeleteManyAsync(f);")]
    [InlineData("await db.GetCollection<CameraGroupDoc>(name).ReplaceOneAsync(f, d);")]
    public void A_camera_write_elsewhere_is_reported_with_its_path_and_line(string line)
    {
        var found = WriteViolations("Gallery.Web/Library/ImageService.cs",
            ["private readonly IMongoCollection<CameraGroupDoc> _cams = x;", line]).ToList();
        Assert.Equal(new[] { $"Gallery.Web/Library/ImageService.cs:2: {line.Trim()}" }, found.ToArray());
    }

    [Theory]
    [InlineData("var all = await _cams.Find(FilterDefinition<CameraGroupDoc>.Empty).ToListAsync();")]
    [InlineData("await _images.UpdateOneAsync(f, u);")]
    public void A_read_or_another_collection_write_is_allowed(string line)
    {
        Assert.Empty(WriteViolations("Gallery.Web/Data/ContentReader.cs",
            ["private readonly IMongoCollection<CameraGroupDoc> _cams = x;", line]));
    }
}

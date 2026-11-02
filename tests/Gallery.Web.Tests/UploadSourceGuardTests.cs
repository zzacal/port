using System.Net;
using System.Xml.Linq;
using Gallery.Core.Ids;
using Gallery.Web.Data;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 008 T047: source guards. III.8 (no server-side image codec, no new package), C3 (no HTML
/// injection sinks in the upload scripts), IV.22 (no admin asset on a public page) and FR-033
/// (the upload code never purges or warms a CDN).
/// </summary>
[Collection("mongo")]
public class UploadSourceGuardTests(MongoFixture mongo)
{
    private static readonly string WebProject = Path.Combine(Render.RepoRoot, "src", "Gallery.Web");

    /// <summary>Gallery.Web.csproj's PackageReference set at 47de295, before 008.</summary>
    private static readonly string[] BaselinePackages =
    [
        "AWSSDK.S3",
        "MetadataExtractor",
        "Microsoft.AspNetCore.Authentication.OpenIdConnect",
        "MongoDB.Driver",
    ];

    private static IEnumerable<string> SourceFiles(string dir, string pattern) =>
        Directory.GetFiles(dir, pattern, SearchOption.AllDirectories)
            .Where(f =>
            {
                var rel = Path.GetRelativePath(WebProject, f).Split(Path.DirectorySeparatorChar);
                return rel[0] != "bin" && rel[0] != "obj";
            })
            .Order(StringComparer.Ordinal);

    private static string Rel(string path) => Path.GetRelativePath(Render.RepoRoot, path);

    // ── III.8 ────────────────────────────────────────────────────────────────

    [Fact]
    public void No_server_source_references_an_image_codec_library()
    {
        var files = SourceFiles(WebProject, "*.cs").ToList();
        Assert.NotEmpty(files);
        var hits = new List<string>();
        foreach (var f in files)
        {
            var text = File.ReadAllText(f);
            foreach (var banned in new[] { "SixLabors", "SkiaSharp", "System.Drawing", "ImageDecoder" })
                if (text.Contains(banned, StringComparison.Ordinal))
                    hits.Add($"{Rel(f)}: {banned}");
        }
        Assert.Empty(hits);
    }

    [Fact]
    public void Web_project_gains_no_package()
    {
        var csproj = XDocument.Load(Path.Combine(WebProject, "Gallery.Web.csproj"));
        var packages = csproj.Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? "")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(BaselinePackages, packages);
    }

    // ── C3 ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("upload.js")]
    [InlineData("upload-worker.js")]
    public void Upload_script_has_no_html_injection_sink(string file)
    {
        var text = File.ReadAllText(Path.Combine(Render.WebRoot, "admin-assets", file));
        Assert.NotEmpty(text);
        foreach (var sink in new[] { "innerHTML", "outerHTML", "insertAdjacentHTML", "document.write" })
            Assert.False(text.Contains(sink, StringComparison.Ordinal), $"admin-assets/{file} uses {sink}");
    }

    // ── FR-033 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Upload_code_never_purges_or_warms_a_cache()
    {
        var files = SourceFiles(Path.Combine(WebProject, "Upload"), "*").ToList();
        Assert.NotEmpty(files);
        var hits = new List<string>();
        foreach (var f in files)
        {
            var text = File.ReadAllText(f);
            foreach (var word in new[] { "Purge", "Warm" })
                if (text.Contains(word, StringComparison.Ordinal))
                    hits.Add($"{Rel(f)}: {word}");
        }
        Assert.Empty(hits);
    }

    // ── IV.22 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Public_pages_reference_no_admin_asset()
    {
        await using var host = await AdminHost.CreateAsync(mongo);
        var db = host.Services.GetRequiredService<IMongoClient>().GetDatabase(host.DatabaseName);

        // The seeded shape: one public gallery with one image, on the home page.
        var galId = OpaqueId.NewId();
        var imgId = OpaqueId.NewId();
        var deriv = new DerivativeDoc { W = 320, Avif = "hash320.avif", Webp = "hash320.webp", BytesAvif = 100, BytesWebp = 80 };
        var ph = new PlaceholderDoc { Color = "#aabbcc", Preview = "data:image/webp;base64,AAAA" };
        var cover = new CoverImageDoc { ImageId = imgId, Width = 1280, Height = 960, Placeholder = ph, Derivatives = [deriv] };
        var card = new GalleryCardDoc { GalleryId = galId, Slug = "autumn-walk", Title = "Autumn Walk", ImageCount = 1, Cover = cover };
        await db.GetCollection<ImageDoc>("images").InsertOneAsync(new ImageDoc
        {
            Id = imgId, SeedKey = imgId, SourceHash = imgId, Title = "Image One", Width = 1280, Height = 960,
            Placeholder = ph, Derivatives = [deriv], GalleryIds = [galId], CreatedAt = DateTime.UtcNow,
        });
        await db.GetCollection<GalleryDoc>("galleries").InsertOneAsync(new GalleryDoc
        {
            Id = galId, SeedKey = "pub1", Slug = "autumn-walk", Title = "Autumn Walk", Visibility = "public",
            Items = [imgId], Card = card, UpdatedAt = DateTime.UtcNow,
        });
        await db.GetCollection<SiteDoc>("site").UpdateOneAsync(
            Builders<SiteDoc>.Filter.Eq(d => d.Id, "site"),
            Builders<SiteDoc>.Update.Set(d => d.HomeOrder, [galId]).Set(d => d.HomeCards, [card]).Set(d => d.UpdatedAt, DateTime.UtcNow),
            new UpdateOptions { IsUpsert = true });

        foreach (var path in new[] { "/", "/g", $"/g/{galId}/autumn-walk", $"/g/{galId}/autumn-walk/{imgId}" })
        {
            var resp = await host.GetAsync(path);
            Assert.True(resp.StatusCode == HttpStatusCode.OK, $"{path} answered {(int)resp.StatusCode}");
            var html = await resp.Content.ReadAsStringAsync();
            if (path != "/") Assert.Contains("Autumn Walk", html); // 011: "/" shows links only
            Assert.False(html.Contains("/admin-assets", StringComparison.Ordinal), $"{path} references /admin-assets");
        }
    }
}

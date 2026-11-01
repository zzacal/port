using System.Collections.Concurrent;
using System.Security.Cryptography;
using Gallery.Web.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace Gallery.Web.Tests.Fixtures;

[CollectionDefinition("browser")]
public class BrowserCollection : ICollectionFixture<BrowserSiteFixture> { }

/// <summary>
/// T038: the real app in a real browser. Starts, once per "browser" collection:
/// - a Mongo container with one public 6-photo gallery and a second 1-photo gallery;
/// - an image host serving real AVIF/WebP ladders (distinct images, so cache hits can't be
///   confused) with the production `public, max-age=31536000, immutable` header. It COUNTS
///   every request per file: a browser-cache hit makes no request, so "served once" proves reuse
///   from the server side, independent of browser internals;
/// - the app (AppBuilder.Build) pointed at both, output cache off;
/// - headless Google Chrome via Playwright (Channel "chrome": the installed Chrome, no browser
///   download). Prerender is disabled so hover/prefetch checks measure the 006 loader alone; with
///   prerender on, Chrome would fetch the destination page's images itself and mask a broken loader.
/// Needs Docker, ffmpeg, cwebp and Google Chrome.
/// </summary>
public sealed class BrowserSiteFixture : IAsyncLifetime
{
    public const int PhotoCount = 6;
    public const string GalleryId = "ppppppppppppppppppppppppp2";
    public const string GallerySlug = "test-roll";
    public const string OtherGalleryId = "qqqqqqqqqqqqqqqqqqqqqqqqq2";
    private static readonly int[] Ladder = [320, 640, 960, 1280];
    private static readonly string[] Colours = ["red", "green", "blue", "orange", "purple", "teal"];

    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:8").Build();
    private readonly ConcurrentDictionary<string, int> _hits = new();
    private WebApplication? _images;
    private WebApplication? _app;
    private IPlaywright? _playwright;
    private string _dir = "";

    public IBrowser Browser { get; private set; } = default!;
    public string BaseUrl { get; private set; } = "";
    public string ImageBaseUrl { get; private set; } = "";
    public IReadOnlyList<string> PhotoIds { get; private set; } = [];

    public string GalleryPath => $"/g/{GalleryId}/{GallerySlug}";
    public string PhotoPath(int i) => $"{GalleryPath}/{PhotoIds[i]}";

    /// <summary>Requests the image host has served for a file name since the last reset.</summary>
    public int Hits(string fileName) => _hits.GetValueOrDefault(fileName);
    public void ResetHits() => _hits.Clear();

    /// <summary>Files requested from the image host since the last reset.</summary>
    public IReadOnlySet<string> HitFiles() => _hits.Keys.ToHashSet();

    public async Task InitializeAsync()
    {
        EncodedFixtures.RequireFfmpeg();
        EncodedFixtures.RequireCwebp();
        _dir = Path.Combine(Path.GetTempPath(), $"gallery-browser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);

        var images = Enumerable.Range(0, PhotoCount).Select(EncodePhoto).ToList();
        PhotoIds = images.Select(i => i.Id).ToList();

        await _mongo.StartAsync();
        var db = new MongoClient(_mongo.GetConnectionString()).GetDatabase("gallery_browser");
        await SeedAsync(db, images);

        _images = StartImageHost();
        await _images.StartAsync();
        ImageBaseUrl = _images.Urls.First();

        // The scoped-CSS bundle is a static web asset: `dotnet run` maps it through the
        // staticwebassets manifest, which a test host only loads in Development with the app's
        // content root. Without it InlineStyles finds no stylesheet and every page renders unstyled.
        _app = Gallery.Web.AppBuilder.Build(
            ["--urls", "http://127.0.0.1:0", "--environment", "Development",
             "--contentRoot", Path.Combine(Render.RepoRoot, "src", "Gallery.Web"),
             "--applicationName", "Gallery.Web"],
            new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = _mongo.GetConnectionString(),
            ["Mongo:Database"] = "gallery_browser",
            ["Storage:ImageBaseUrl"] = ImageBaseUrl,
            ["Site:OutputCache"] = "false",
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging:LogLevel:Microsoft.AspNetCore.Diagnostics"] = "Error",
        });
        await _app.StartAsync();
        BaseUrl = _app.Urls.First();

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new()
        {
            Channel = "chrome",
            Headless = true,
            Args = ["--disable-features=Prerender2"],
        });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.DisposeAsync();
        _playwright?.Dispose();
        if (_app is not null) await _app.DisposeAsync();
        if (_images is not null) await _images.DisposeAsync();
        await _mongo.DisposeAsync();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    // A browser context per test: its own HTTP cache, so tests can't warm each other.
    public Task<IBrowserContext> NewContextAsync(int width = 1440, int height = 900, bool javaScript = true)
        => Browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = height }, JavaScriptEnabled = javaScript });

    private WebApplication StartImageHost()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var host = builder.Build();
        host.MapGet("/{file}", async (string file, HttpContext ctx) =>
        {
            var path = Path.Combine(_dir, Path.GetFileName(file));
            if (!File.Exists(path)) return Results.NotFound();
            _hits.AddOrUpdate(file, 1, (_, n) => n + 1);
            ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            var type = file.EndsWith(".avif", StringComparison.Ordinal) ? "image/avif" : "image/webp";
            return Results.Bytes(await File.ReadAllBytesAsync(path), type);
        });
        return host;
    }

    private sealed record Encoded(string Id, int Width, int Height, List<DerivativeDoc> Derivatives, string Preview);

    // A distinct 1600x1200 image per index, encoded like scripts/prepare-photos.sh.
    private Encoded EncodePhoto(int index)
    {
        var png = Path.Combine(_dir, $"src-{index}.png");
        Run("/opt/homebrew/bin/ffmpeg", $"-hide_banner -loglevel error -y -f lavfi -i \"color=c={Colours[index]}:s=1600x1200,drawbox=x={index * 200}:y=100:w=300:h=900:c=white:t=fill\" -frames:v 1 \"{png}\"");
        var derivatives = new List<DerivativeDoc>();
        foreach (var w in Ladder)
        {
            var avif = Named(Path.Combine(_dir, $"tmp-{index}-{w}.avif"), path =>
                Run("/opt/homebrew/bin/ffmpeg", $"-hide_banner -loglevel error -y -i \"{png}\" -vf \"scale={w}:-2,format=yuv420p\" -frames:v 1 -map_metadata -1 -c:v libsvtav1 -crf 32 -preset 8 \"{path}\""));
            var webp = Named(Path.Combine(_dir, $"tmp-{index}-{w}.webp"), path =>
                Run("/opt/homebrew/bin/cwebp", $"-quiet -q 78 -resize {w} 0 \"{png}\" -o \"{path}\""));
            derivatives.Add(new DerivativeDoc { W = w, Avif = avif, Webp = webp, BytesAvif = 1, BytesWebp = 1 });
        }
        var preview = Path.Combine(_dir, $"preview-{index}.webp");
        Run("/opt/homebrew/bin/cwebp", $"-quiet -q 40 -resize 32 24 \"{png}\" -o \"{preview}\"");
        var id = new string((char)('a' + index), 25) + "2";
        return new Encoded(id, 1280, 960, derivatives, "data:image/webp;base64," + Convert.ToBase64String(File.ReadAllBytes(preview)));
    }

    // Content-hash name, as the real pipeline does: identical bytes would share a cache entry.
    private string Named(string tmpPath, Action<string> encode)
    {
        encode(tmpPath);
        var ext = Path.GetExtension(tmpPath);
        var name = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(tmpPath)))[..16] + ext;
        File.Move(tmpPath, Path.Combine(_dir, name), overwrite: true);
        return name;
    }

    private static async Task SeedAsync(IMongoDatabase db, List<Encoded> images)
    {
        PlaceholderDoc Ph(Encoded e) => new() { Color = "#808080", Preview = e.Preview };
        GalleryCardDoc Card(string gid, string slug, string title, Encoded cover, int count) => new()
        {
            GalleryId = gid, Slug = slug, Title = title, ImageCount = count,
            Cover = new CoverImageDoc { ImageId = cover.Id, Width = cover.Width, Height = cover.Height, Placeholder = Ph(cover), Derivatives = cover.Derivatives },
        };

        await db.GetCollection<ImageDoc>("images").InsertManyAsync(images.Select((e, i) => new ImageDoc
        {
            Id = e.Id, SeedKey = e.Id, SourceHash = e.Id, Title = $"Photo {i + 1}", Width = e.Width, Height = e.Height,
            Placeholder = Ph(e), Derivatives = e.Derivatives, GalleryIds = [GalleryId], CreatedAt = DateTime.UtcNow,
            Caption = i == 0 ? "First caption." : null,
            Camera = new CameraDoc { Make = "TESTCAM", Model = "T1", FNumber = 4m, Iso = 100 },
        }));

        var main = Card(GalleryId, GallerySlug, "Test Roll", images[0], images.Count);
        var other = Card(OtherGalleryId, "second", "Second", images[1], 1);
        await db.GetCollection<GalleryDoc>("galleries").InsertManyAsync(
        [
            new GalleryDoc { Id = GalleryId, SeedKey = "roll", Slug = GallerySlug, Title = "Test Roll", Visibility = "public", Items = images.Select(e => e.Id).ToList(), Card = main, UpdatedAt = DateTime.UtcNow },
            new GalleryDoc { Id = OtherGalleryId, SeedKey = "second", Slug = "second", Title = "Second", Visibility = "public", Items = [images[1].Id], Card = other, UpdatedAt = DateTime.UtcNow },
        ]);
        await db.GetCollection<SiteDoc>("site").InsertOneAsync(new SiteDoc
        {
            HomeOrder = [GalleryId, OtherGalleryId], HomeCards = [main, other], UpdatedAt = DateTime.UtcNow,
        });
    }

    private static void Run(string exe, string args)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, args)
        {
            RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false,
        })!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"{exe} failed: {err}");
    }
}

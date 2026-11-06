using System.Collections.Concurrent;
using Gallery.Web.Data;
using Gallery.Web.Library;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace Gallery.Web.Tests.Fixtures;

[CollectionDefinition("nav-browser")]
public class NavBrowserCollection : ICollectionFixture<NavBrowserFixture> { }

/// <summary>
/// 010 T036 (research R-13): the navigation features in a real browser. Modelled on
/// <see cref="BrowserSiteFixture"/> (which stays untouched). Starts, once per "nav-browser" collection:
/// - a Mongo container holding 23 image documents that reuse 6 encoded derivative sets cyclically
///   (<see cref="PhotoEncoder"/>), and four galleries in this home order: "September 6" (all 23),
///   "Coast" (images 0-5), "Solo" (image 6), all public, then "Private" (images 7-8, private);
/// - an image host with the production <c>public, max-age=31536000, immutable</c> header that counts
///   every request per file (a browser-cache hit makes no request);
/// - the app (AppBuilder.Build, Development, the app's content root, output cache off).
///   Cards come from <see cref="CardBuilder.Build"/> per gallery and <c>homeCards</c>/<c>nav</c> from
///   the app's own <see cref="SiteProjector"/>: no hand-written card or nav;
/// - two headless Google Chromes (Channel "chrome"): <see cref="Browser"/> with Prerender2 disabled
///   (deterministic navigations and hit counts, as BrowserSiteFixture), and
///   <see cref="PrerenderBrowser"/> with prerender left on, for the R-01-a activation case.
/// <see cref="RenameAsync"/>, <see cref="RemovePhotoAsync"/> and <see cref="RestorePhotoAsync"/> go
/// through the app's <see cref="GalleryService"/>.
/// </summary>
public sealed class NavBrowserFixture : IAsyncLifetime
{
    public const int PhotoCount = 23;
    public const string SeptemberId = "sssssssssssssssssssssssss2";
    public const string CoastId = "ccccccccccccccccccccccccc2";
    public const string SoloId = "ooooooooooooooooooooooooo2";
    public const string PrivateId = "ppppppppppppppppppppppppp3";
    public const string SeptemberTitle = "September 6";

    private const string DbName = "gallery_nav_browser";

    private MongoDbContainer _mongo = null!;
    private readonly ConcurrentDictionary<string, int> _hits = new();
    private WebApplication? _images;
    private WebApplication? _app;
    private IPlaywright? _playwright;
    private string _dir = "";

    public IBrowser Browser { get; private set; } = default!;
    public IBrowser PrerenderBrowser { get; private set; } = default!;
    public string BaseUrl { get; private set; } = "";
    public string ImageBaseUrl { get; private set; } = "";

    /// <summary>The 23 image ids, in "September 6" order.</summary>
    public IReadOnlyList<string> PhotoIds { get; private set; } = [];
    public IReadOnlyList<string> CoastPhotoIds => PhotoIds.Take(6).ToList();
    public string SoloPhotoId => PhotoIds[6];

    public IMongoDatabase Db { get; private set; } = default!;

    /// <summary>The CURRENT slug of a gallery, read from Mongo (a rename changes it).</summary>
    public async Task<string> SlugAsync(string gid) =>
        (await Db.GetCollection<GalleryDoc>("galleries").Find(g => g.Id == gid).FirstAsync()).Slug;

    public async Task<IReadOnlyList<string>> ItemsAsync(string gid) =>
        (await Db.GetCollection<GalleryDoc>("galleries").Find(g => g.Id == gid).FirstAsync()).Items;

    public async Task<string> GalleryPathAsync(string gid) => $"/g/{gid}/{await SlugAsync(gid)}";
    public async Task<string> PhotoPathAsync(int i) => $"{await GalleryPathAsync(SeptemberId)}/{PhotoIds[i]}";

    // 013 (test-impact C): every fixture photo is TESTCAM T1, so the one stored camera is testcam-t1.
    public const string CameraSlug = "testcam-t1";
    public Task<string> CameraPathAsync() => Task.FromResult($"/c/{CameraSlug}");

    /// <summary>The camera photo page of the stored camera's <paramref name="i"/>-th item.</summary>
    public async Task<string> CameraPhotoPathAsync(int i)
    {
        var cam = await Db.GetCollection<CameraGroupDoc>("cameras").Find(c => c.Slug == CameraSlug).FirstAsync();
        return $"/c/{CameraSlug}/{cam.Items[i]}";
    }

    /// <summary>Requests the image host has served for a file name since the last reset.</summary>
    public int Hits(string fileName) => _hits.GetValueOrDefault(fileName);
    public void ResetHits() => _hits.Clear();
    public IReadOnlySet<string> HitFiles() => _hits.Keys.ToHashSet();

    public async Task InitializeAsync()
    {
        EncodedFixtures.RequireFfmpeg();
        EncodedFixtures.RequireCwebp();
        _dir = Path.Combine(Path.GetTempPath(), $"gallery-nav-browser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);

        var sets = Enumerable.Range(0, 6).Select(i => PhotoEncoder.EncodePhoto(_dir, i)).ToList();
        // 23 valid opaque ids ([a-z2-7]{26}), distinct from the encoder's own ids.
        PhotoIds = Enumerable.Range(0, PhotoCount).Select(i => new string('n', 24) + "a" + (char)('a' + i)).ToList();

        _mongo = await MongoFixture.StartContainerAsync();
        Db = new MongoClient(_mongo.GetConnectionString()).GetDatabase(DbName);

        _images = StartImageHost();
        await _images.StartAsync();
        ImageBaseUrl = _images.Urls.First();

        _app = Gallery.Web.AppBuilder.Build(
            ["--urls", "http://127.0.0.1:0", "--environment", "Development",
             "--contentRoot", Path.Combine(Render.RepoRoot, "src", "Gallery.Web"),
             "--applicationName", "Gallery.Web"],
            new Dictionary<string, string?>
            {
                ["Mongo:ConnectionString"] = _mongo.GetConnectionString(),
                ["Mongo:Database"] = DbName,
                ["Storage:ImageBaseUrl"] = ImageBaseUrl,
                ["Site:OutputCache"] = "false",
                ["Logging:LogLevel:Default"] = "Warning",
                ["Logging:LogLevel:Microsoft.AspNetCore.Diagnostics"] = "Error",
            });
        await _app.StartAsync();
        BaseUrl = _app.Urls.First();

        await SeedAsync(sets);

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new()
        {
            Channel = "chrome",
            Headless = true,
            Args = ["--disable-features=Prerender2"],
        });
        PrerenderBrowser = await _playwright.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (PrerenderBrowser is not null) await PrerenderBrowser.DisposeAsync();
        if (Browser is not null) await Browser.DisposeAsync();
        _playwright?.Dispose();
        if (_app is not null) await _app.DisposeAsync();
        if (_images is not null) await _images.DisposeAsync();
        if (_mongo is not null) await _mongo.DisposeAsync();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    /// <summary>A context per test (its own HTTP cache), on the prerender-disabled browser by default.</summary>
    public Task<IBrowserContext> NewContextAsync(int width = 1440, int height = 900, bool javaScript = true, bool prerender = false)
        => (prerender ? PrerenderBrowser : Browser).NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = height }, JavaScriptEnabled = javaScript,
        });

    // ── owner writes, through the app's own GalleryService ──────────────────────────────────────

    /// <summary>Renames a gallery (title and slug, P-RENAME) and returns the new slug.</summary>
    public async Task<string> RenameAsync(string gid, string title)
    {
        using (var scope = _app!.Services.CreateScope())
        {
            var r = await scope.ServiceProvider.GetRequiredService<GalleryService>().UpdateTextAsync(gid, title, null);
            if (r.Status != OperationStatus.Ok) throw new InvalidOperationException($"Rename of {gid} answered {r.Status}: {r.Reason}");
        }
        return await SlugAsync(gid);
    }

    /// <summary>Removes one photo from a gallery (B5).</summary>
    public async Task RemovePhotoAsync(string gid, string iid)
    {
        using var scope = _app!.Services.CreateScope();
        var r = await scope.ServiceProvider.GetRequiredService<GalleryService>().RemoveAsync(gid, iid);
        if (r.Status != OperationStatus.Ok || r.Removed != 1)
            throw new InvalidOperationException($"Remove of {iid} from {gid} answered {r.Status} (removed {r.Removed}): {r.Reason}");
    }

    /// <summary>Puts a removed photo back and restores <paramref name="order"/> (add, then reorder).</summary>
    public async Task RestorePhotoAsync(string gid, string iid, IReadOnlyList<string> order)
    {
        using var scope = _app!.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<GalleryService>();
        var added = await service.AddAsync(gid, [iid]);
        if (added.Status != OperationStatus.Ok || added.Added != 1)
            throw new InvalidOperationException($"Re-adding {iid} to {gid} answered {added.Status} (added {added.Added})");
        var reordered = await service.ReorderAsync(gid, order);
        if (reordered.Status != OperationStatus.Ok)
            throw new InvalidOperationException($"Restoring the order of {gid} answered {reordered.Status}: {reordered.Reason}");
    }

    // ── setup ──────────────────────────────────────────────────────────────────────────────────

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

    private async Task SeedAsync(List<PhotoEncoder.Encoded> sets)
    {
        var galleries = new List<GalleryDoc>
        {
            NewGallery(SeptemberId, "september-6", SeptemberTitle, "public", PhotoIds),
            NewGallery(CoastId, "coast", "Coast", "public", PhotoIds.Take(6)),
            NewGallery(SoloId, "solo", "Solo", "public", [PhotoIds[6]]),
            NewGallery(PrivateId, "private", "Private", "private", [PhotoIds[7], PhotoIds[8]]),
        };

        var images = PhotoIds.Select((id, i) =>
        {
            var e = sets[i % sets.Count];
            return new ImageDoc
            {
                Id = id, SeedKey = id, SourceHash = id, Title = $"Photo {i + 1}", Width = e.Width, Height = e.Height,
                Placeholder = new PlaceholderDoc { Color = "#808080", Preview = e.Preview },
                Derivatives = [.. e.Derivatives],
                GalleryIds = galleries.Where(g => g.Items.Contains(id)).Select(g => g.Id).ToList(),
                CreatedAt = DateTime.UtcNow,
                Camera = new CameraDoc { Make = "TESTCAM", Model = "T1", FNumber = 4m, Iso = 100 },
            };
        }).ToList();
        await Db.GetCollection<ImageDoc>("images").InsertManyAsync(images);

        // THE card function, per gallery, with its cover image document.
        var byId = images.ToDictionary(i => i.Id);
        foreach (var g in galleries)
            g.Card = CardBuilder.Build(g, CardBuilder.CoverId(g) is { } cover ? byId[cover] : null);
        await Db.GetCollection<GalleryDoc>("galleries").InsertManyAsync(galleries);

        // homeOrder only; homeCards and every gallery's nav come from the app's projector.
        await Db.GetCollection<SiteDoc>("site").InsertOneAsync(new SiteDoc
        {
            HomeOrder = galleries.Select(g => g.Id).ToList(), HomeCards = [], UpdatedAt = DateTime.UtcNow,
        });
        using var scope = _app!.Services.CreateScope();
        var projected = await scope.ServiceProvider.GetRequiredService<SiteProjector>().ProjectWithNavAsync();
        if (projected.Cards.Count != 3 || projected.NavChanged.Count != 3)
            throw new InvalidOperationException(
                $"Projection gave {projected.Cards.Count} cards and {projected.NavChanged.Count} nav writes; expected 3 and 3.");
    }

    private static GalleryDoc NewGallery(string id, string slug, string title, string visibility, IEnumerable<string> items) => new()
    {
        Id = id, SeedKey = slug, Slug = slug, Title = title, Visibility = visibility, Items = items.ToList(), UpdatedAt = DateTime.UtcNow,
    };
}

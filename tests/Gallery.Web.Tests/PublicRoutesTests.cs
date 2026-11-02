using Gallery.Web.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// Integration tests for public routes (T023, T024, T025).
/// Uses real app with AppBuilder.Build + MongoFixture (Testcontainers).
/// </summary>
[Collection("mongo")]
public class PublicRoutesTests(MongoFixture mongo) : IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient? _client;
    private IMongoDatabase? _db;

    // ── per-test setup helpers ────────────────────────────────────────────────

    private async Task<(WebApplication app, HttpClient client, IMongoDatabase db)> StartAppAsync(
        Dictionary<string, string?>? extraOverrides = null)
    {
        var db = mongo.NewDatabase();
        var overrides = new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = mongo.ConnectionString,
            ["Mongo:Database"] = db.DatabaseNamespace.DatabaseName,
            ["Storage:ImageBaseUrl"] = "https://img.test",
            ["Storage:ServiceUrl"] = "http://localhost:19999",   // unused in tests
            ["Storage:AccessKeyId"] = "local",
            ["Storage:SecretAccessKey"] = "localsecret",
            ["Storage:PublicBucket"] = "pub",
            ["Storage:PrivateBucket"] = "priv",
            ["Site:OutputCache"] = "false",
        };
        if (extraOverrides is not null)
            foreach (var (k, v) in extraOverrides)
                overrides[k] = v;

        var app = Gallery.Web.AppBuilder.Build(["--urls", "http://127.0.0.1:0"], overrides);
        await app.StartAsync();

        var baseAddress = new Uri(app.Urls.First());
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = baseAddress
        };
        return (app, client, db);
    }

    public async Task InitializeAsync()
    {
        (_app, _client, _db) = await StartAppAsync();
        await SeedTestDataAsync(_db);
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null) await _app.StopAsync();
        if (_app is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
    }

    // ── test data ─────────────────────────────────────────────────────────────

    // IDs (valid 26-char base32)
    private const string GalleryPubId1  = "aaaaaaaaaaaaaaaaaaaaaaaa22";
    private const string GalleryPubId2  = "bbbbbbbbbbbbbbbbbbbbbbbb22";
    private const string GalleryPrivId  = "cccccccccccccccccccccccc22";
    private const string GalleryEmptyId = "dddddddddddddddddddddddd22";
    private const string ImgId1 = "eeeeeeeeeeeeeeeeeeeeeeee22";
    private const string ImgId2 = "ffffffffffffffffffffff2222";
    private const string ImgId3 = "gggggggggggggggggggggggg22";

    private static Gallery.Web.Data.DerivativeDoc MakeDeriv(int w) => new()
    {
        W = w,
        Avif = $"hash{w}.avif",
        Webp = $"hash{w}.webp",
        BytesAvif = 100,
        BytesWebp = 80
    };

    private static Gallery.Web.Data.PlaceholderDoc Placeholder() => new()
    {
        Color = "#aabbcc",
        Preview = "data:image/webp;base64,AAAA"
    };

    private static Gallery.Web.Data.ImageDoc MakeImageDoc(string id, string title, string? caption = null) => new()
    {
        Id = id,
        SeedKey = id,
        SourceHash = id,
        Title = title,
        Caption = caption,
        Width = 1280,
        Height = 960,
        Placeholder = Placeholder(),
        Derivatives = [MakeDeriv(320), MakeDeriv(640), MakeDeriv(1280)],
        GalleryIds = [],
        CreatedAt = DateTime.UtcNow,
    };

    private static Gallery.Web.Data.CoverImageDoc MakeCover(string imgId) => new()
    {
        ImageId = imgId,
        Width = 1280,
        Height = 960,
        Placeholder = Placeholder(),
        Derivatives = [MakeDeriv(320), MakeDeriv(640), MakeDeriv(1280)],
    };

    private static Gallery.Web.Data.GalleryCardDoc MakeCard(string galId, string slug, string title, string? coverId = null) => new()
    {
        GalleryId = galId,
        Slug = slug,
        Title = title,
        ImageCount = coverId is null ? 0 : 1,
        Cover = coverId is null ? null : MakeCover(coverId),
    };

    private async Task SeedTestDataAsync(IMongoDatabase db)
    {
        var galleries = db.GetCollection<Gallery.Web.Data.GalleryDoc>("galleries");
        var images = db.GetCollection<Gallery.Web.Data.ImageDoc>("images");
        var site = db.GetCollection<Gallery.Web.Data.SiteDoc>("site");

        // 3 images
        await images.InsertManyAsync([
            MakeImageDoc(ImgId1, "Image One", "Caption for image one"),
            MakeImageDoc(ImgId2, "Image Two"),
            MakeImageDoc(ImgId3, "Image Three"),
        ]);

        // Public gallery 1: 3 images
        await galleries.InsertOneAsync(new Gallery.Web.Data.GalleryDoc
        {
            Id = GalleryPubId1,
            SeedKey = "pub1",
            Slug = "autumn-walk",
            Title = "Autumn Walk",
            Description = "Beautiful autumn scenery",
            Visibility = "public",
            Items = [ImgId1, ImgId2, ImgId3],
            Card = MakeCard(GalleryPubId1, "autumn-walk", "Autumn Walk", ImgId1),
            UpdatedAt = DateTime.UtcNow,
        });

        // Public gallery 2: 1 image
        await galleries.InsertOneAsync(new Gallery.Web.Data.GalleryDoc
        {
            Id = GalleryPubId2,
            SeedKey = "pub2",
            Slug = "spring-meadow",
            Title = "Spring Meadow",
            Visibility = "public",
            Items = [ImgId1],
            Card = MakeCard(GalleryPubId2, "spring-meadow", "Spring Meadow", ImgId1),
            UpdatedAt = DateTime.UtcNow,
        });

        // Private gallery: 1 image
        await galleries.InsertOneAsync(new Gallery.Web.Data.GalleryDoc
        {
            Id = GalleryPrivId,
            SeedKey = "priv1",
            Slug = "private-gallery",
            Title = "Private Gallery",
            Visibility = "private",
            Items = [ImgId1],
            Card = MakeCard(GalleryPrivId, "private-gallery", "Private Gallery", ImgId1),
            UpdatedAt = DateTime.UtcNow,
        });

        // Empty public gallery
        await galleries.InsertOneAsync(new Gallery.Web.Data.GalleryDoc
        {
            Id = GalleryEmptyId,
            SeedKey = "empty1",
            Slug = "coming-soon",
            Title = "Coming Soon",
            Visibility = "public",
            Items = [],
            Card = MakeCard(GalleryEmptyId, "coming-soon", "Coming Soon"),
            UpdatedAt = DateTime.UtcNow,
        });

        // Home cards: only public ones
        await site.InsertOneAsync(new Gallery.Web.Data.SiteDoc
        {
            Id = "site",
            HomeOrder = [GalleryPubId1, GalleryPubId2, GalleryEmptyId],
            HomeCards = [
                MakeCard(GalleryPubId1, "autumn-walk", "Autumn Walk", ImgId1),
                MakeCard(GalleryPubId2, "spring-meadow", "Spring Meadow", ImgId1),
                MakeCard(GalleryEmptyId, "coming-soon", "Coming Soon"),
            ],
            UpdatedAt = DateTime.UtcNow,
        });
    }

    // ── T023: basic public routes ─────────────────────────────────────────────

    [Fact]
    public async Task Home_lists_public_cards_in_order_with_one_db_read()
    {
        var resp = await _client!.GetAsync("/g");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();

        // Public gallery cards in order
        var pos1 = html.IndexOf("Autumn Walk", StringComparison.Ordinal);
        var pos2 = html.IndexOf("Spring Meadow", StringComparison.Ordinal);
        Assert.True(pos1 >= 0 && pos2 >= 0 && pos1 < pos2);

        // 1 db read
        var serverTiming = resp.Headers.GetValues("Server-Timing").ToList();
        var dbCount = serverTiming.Count(h => h.Contains("db;dur="));
        Assert.Equal(1, dbCount);

        // Cache-Control present
        Assert.Equal(Gallery.Web.Site.PublicCache.HtmlPolicy, resp.Headers.GetValues("Cache-Control").First());

        // No Set-Cookie
        Assert.False(resp.Headers.TryGetValues("Set-Cookie", out _));
    }

    [Fact]
    public async Task Gallery_page_has_images_in_order_and_2_db_reads()
    {
        var resp = await _client!.GetAsync($"/g/{GalleryPubId1}/autumn-walk");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();

        // Images in order
        var p1 = html.IndexOf("Image One", StringComparison.Ordinal);
        var p2 = html.IndexOf("Image Two", StringComparison.Ordinal);
        var p3 = html.IndexOf("Image Three", StringComparison.Ordinal);
        Assert.True(p1 >= 0 && p2 > p1 && p3 > p2);

        // 2 db;dur= entries
        var serverTiming = string.Join(",", resp.Headers.GetValues("Server-Timing"));
        var dbCount = CountOccurrences(serverTiming, "db;dur=");
        Assert.Equal(2, dbCount);

        // Cache-Control and Link headers present
        Assert.True(resp.Headers.Contains("Cache-Control"));
        Assert.True(resp.Headers.Contains("Link"));

        // No Set-Cookie
        Assert.False(resp.Headers.TryGetValues("Set-Cookie", out _));
    }

    [Fact]
    public async Task Photo_page_has_correct_prev_next_caption_and_prefetch()
    {
        // First photo: no prev, has next
        var resp = await _client!.GetAsync($"/g/{GalleryPubId1}/autumn-walk/{ImgId1}");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();

        // Caption
        Assert.Contains("Caption for image one", html);

        // Prefetch: has next neighbor, no prev
        Assert.Contains("data-prefetch=", html);
        Assert.Contains("img.test", html);

        // Last photo: has prev, no next
        var resp3 = await _client!.GetAsync($"/g/{GalleryPubId1}/autumn-walk/{ImgId3}");
        var html3 = await resp3.Content.ReadAsStringAsync();
        Assert.Contains("data-prefetch=", html3);

        // Cache-Control present
        Assert.Equal(Gallery.Web.Site.PublicCache.HtmlPolicy, resp.Headers.GetValues("Cache-Control").First());
        Assert.False(resp.Headers.TryGetValues("Set-Cookie", out _));
    }

    [Fact]
    public async Task All_img_and_source_urls_start_with_ImageBaseUrl()
    {
        var resp = await _client!.GetAsync($"/g/{GalleryPubId1}/autumn-walk");
        var html = await resp.Content.ReadAsStringAsync();

        // Find all src= and srcset= values
        var srcPattern = new System.Text.RegularExpressions.Regex(@"src=""([^""]+)""");
        var srcsetPattern = new System.Text.RegularExpressions.Regex(@"srcset=""([^""]+)""");

        foreach (System.Text.RegularExpressions.Match m in srcPattern.Matches(html))
        {
            var val = m.Groups[1].Value;
            if (val.StartsWith("data:") || val.StartsWith("/samples") || val.StartsWith("/")) continue;
            Assert.StartsWith("https://img.test", val);
        }

        foreach (System.Text.RegularExpressions.Match m in srcsetPattern.Matches(html))
        {
            var parts = m.Groups[1].Value.Split(',');
            foreach (var part in parts)
            {
                var url = part.Trim().Split(' ')[0];
                if (url.StartsWith("/") || url.StartsWith("data:")) continue;
                Assert.StartsWith("https://img.test", url);
            }
        }
    }

    [Fact]
    public async Task Datastore_down_returns_500_with_no_stack_trace()
    {
        // Create a second app with a broken Mongo connection
        var (downApp, downClient, _) = await StartAppAsync(new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = "mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=500",
            ["Mongo:Database"] = "gallery_test_down",
        });
        try
        {
            var resp = await downClient.GetAsync($"/g/{GalleryPubId1}/autumn-walk");
            Assert.Equal(System.Net.HttpStatusCode.InternalServerError, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Equal("Something went wrong", body);
            Assert.DoesNotContain("at ", body);
        }
        finally
        {
            downClient.Dispose();
            await downApp.StopAsync();
            if (downApp is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
        }
    }

    // ── T024: [SAFETY] private gallery noindex ────────────────────────────────

    [Fact]
    public async Task Private_gallery_absent_from_home()
    {
        var resp = await _client!.GetAsync("/g");
        var html = await resp.Content.ReadAsStringAsync();

        // Private title not in home (it's not in homeCards)
        Assert.DoesNotContain("Private Gallery", html);
        // Private id not as link
        Assert.DoesNotContain(GalleryPrivId, html);
    }

    [Fact]
    public async Task Private_gallery_page_returns_200_with_noindex()
    {
        var resp = await _client!.GetAsync($"/g/{GalleryPrivId}/private-gallery");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();

        // noindex meta tag (Razor adds scoped CSS b-xxxx attribute, so check partial)
        Assert.Contains("content=\"noindex\"", html);
        // X-Robots-Tag header
        Assert.Equal("noindex", resp.Headers.GetValues("X-Robots-Tag").First());
    }

    [Fact]
    public async Task Private_photo_page_carries_noindex()
    {
        var resp = await _client!.GetAsync($"/g/{GalleryPrivId}/private-gallery/{ImgId1}");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();

        Assert.Contains("content=\"noindex\"", html);
        Assert.Equal("noindex", resp.Headers.GetValues("X-Robots-Tag").First());
    }

    [Fact]
    public async Task Public_gallery_pages_carry_no_noindex()
    {
        var gallResp = await _client!.GetAsync($"/g/{GalleryPubId1}/autumn-walk");
        Assert.Equal(System.Net.HttpStatusCode.OK, gallResp.StatusCode);
        var gallHtml = await gallResp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("content=\"noindex\"", gallHtml);
        Assert.False(gallResp.Headers.TryGetValues("X-Robots-Tag", out _));

        var photoResp = await _client!.GetAsync($"/g/{GalleryPubId1}/autumn-walk/{ImgId1}");
        var photoHtml = await photoResp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("content=\"noindex\"", photoHtml);
        Assert.False(photoResp.Headers.TryGetValues("X-Robots-Tag", out _));
    }

    // ── T025: slugs survive renames, bad addresses fail cleanly ──────────────

    [Fact]
    public async Task Wrong_slug_gives_301_to_correct_slug()
    {
        var resp = await _client!.GetAsync($"/g/{GalleryPubId1}/wrong-slug");
        Assert.Equal(System.Net.HttpStatusCode.MovedPermanently, resp.StatusCode);
        var location = resp.Headers.GetValues("Location").First();
        Assert.Equal($"/g/{GalleryPubId1}/autumn-walk", location);
        Assert.Equal(Gallery.Web.Site.PublicCache.ShortPolicy, resp.Headers.GetValues("Cache-Control").First());
    }

    [Fact]
    public async Task Gallery_short_url_gives_301_to_slug()
    {
        var resp = await _client!.GetAsync($"/g/{GalleryPubId1}");
        Assert.Equal(System.Net.HttpStatusCode.MovedPermanently, resp.StatusCode);
        var location = resp.Headers.GetValues("Location").First();
        Assert.Equal($"/g/{GalleryPubId1}/autumn-walk", location);
        Assert.Equal(Gallery.Web.Site.PublicCache.ShortPolicy, resp.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Photo_wrong_slug_gives_301_with_correct_slug_and_same_imageId()
    {
        var resp = await _client!.GetAsync($"/g/{GalleryPubId1}/wrong/{ImgId1}");
        Assert.Equal(System.Net.HttpStatusCode.MovedPermanently, resp.StatusCode);
        var location = resp.Headers.GetValues("Location").First();
        Assert.Equal($"/g/{GalleryPubId1}/autumn-walk/{ImgId1}", location);
    }

    [Fact]
    public async Task Unknown_valid_id_gives_404_with_noindex_and_1_db_read()
    {
        const string unknownId = "zzzzzzzzzzzzzzzzzzzzzzzz22";
        var resp = await _client!.GetAsync($"/g/{unknownId}/some-slug");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Not found", html);
        Assert.Equal("noindex", resp.Headers.GetValues("X-Robots-Tag").First());

        var serverTiming = string.Join(",", resp.Headers.GetValues("Server-Timing"));
        var dbCount = CountOccurrences(serverTiming, "db;dur=");
        Assert.Equal(1, dbCount);
    }

    [Fact]
    public async Task Malformed_id_gives_404_with_zero_db_reads()
    {
        // ABC - uppercase, malformed
        var resp1 = await _client!.GetAsync("/g/ABC/slug");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, resp1.StatusCode);
        var serverTiming1 = string.Join(",", resp1.Headers.GetValues("Server-Timing"));
        Assert.Equal(0, CountOccurrences(serverTiming1, "db;dur="));

        // 25 chars
        var resp2 = await _client!.GetAsync("/g/aaaaaaaaaaaaaaaaaaaaaaaaa/slug");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, resp2.StatusCode);
        var serverTiming2 = string.Join(",", resp2.Headers.GetValues("Server-Timing"));
        Assert.Equal(0, CountOccurrences(serverTiming2, "db;dur="));

        // Contains '0'
        var resp3 = await _client!.GetAsync("/g/aaaaaaaaaaaaaaaaaaaaaaaaa0/slug");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, resp3.StatusCode);
        var serverTiming3 = string.Join(",", resp3.Headers.GetValues("Server-Timing"));
        Assert.Equal(0, CountOccurrences(serverTiming3, "db;dur="));
    }

    [Fact]
    public async Task ImageId_not_in_items_gives_404()
    {
        const string unknownImgId = "zzzzzzzzzzzzzzzzzzzzzzz222";
        var resp = await _client!.GetAsync($"/g/{GalleryPubId1}/autumn-walk/{unknownImgId}");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Redirect_and_404_cache_control_equals_ShortPolicy()
    {
        // 301
        var redirect = await _client!.GetAsync($"/g/{GalleryPubId1}/wrong-slug");
        Assert.Equal(Gallery.Web.Site.PublicCache.ShortPolicy, redirect.Headers.GetValues("Cache-Control").First());

        // 404
        var notFound = await _client!.GetAsync("/g/ABC/slug");
        Assert.Equal(Gallery.Web.Site.PublicCache.ShortPolicy, notFound.Headers.GetValues("Cache-Control").First());
    }

    // ── utilities ─────────────────────────────────────────────────────────────

    private static int CountOccurrences(string s, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = s.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0) { count++; idx++; }
        return count;
    }

    // ── R-10: pages render without storage credentials ────────────────────────────────────────
    // Only ImageBaseUrl is needed to build image URLs. Found by the browser fixture (T038), which
    // sets no credentials: every page 500'd because the endpoints resolved ImageStore, whose
    // constructor builds an S3 client. The other tests here pass dummy credentials and missed it.

    [Fact]
    public async Task Public_pages_render_with_no_storage_credentials()
    {
        var db = mongo.NewDatabase();
        await SeedTestDataAsync(db);
        var app = Gallery.Web.AppBuilder.Build(["--urls", "http://127.0.0.1:0"], new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = mongo.ConnectionString,
            ["Mongo:Database"] = db.DatabaseNamespace.DatabaseName,
            ["Storage:ImageBaseUrl"] = "https://img.test",
            ["Storage:ServiceUrl"] = "",
            ["Storage:AccessKeyId"] = "",
            ["Storage:SecretAccessKey"] = "",
            ["Site:OutputCache"] = "false",
        });
        await app.StartAsync();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
            foreach (var path in new[] { "/", "/g", $"/g/{GalleryPubId1}/autumn-walk", $"/g/{GalleryPubId1}/autumn-walk/{ImgId2}" })
            {
                var resp = await http.GetAsync(path);
                Assert.True(resp.StatusCode == System.Net.HttpStatusCode.OK, $"{path} -> {(int)resp.StatusCode}");
                if (path != "/") Assert.Contains("https://img.test/", await resp.Content.ReadAsStringAsync()); // 011: "/" has no image
            }
        }
        finally { await app.StopAsync(); await app.DisposeAsync(); }
    }

    // ── II.6: visitors never see an empty gallery ────────────────────────────────
    // The seeded site doc deliberately still lists the empty public gallery: the home page must
    // drop it even when the stored listing is stale, and its page must 404.

    [Fact]
    public async Task Empty_public_gallery_is_not_on_the_home_page()
    {
        var html = await _client!.GetStringAsync("/g");
        Assert.DoesNotContain(GalleryEmptyId, html);
        Assert.DoesNotContain("Coming Soon", html);
        Assert.DoesNotContain("tile-empty", html);
        Assert.Contains("Autumn Walk", html); // the non-empty public galleries are still listed
    }

    [Fact]
    public async Task Empty_gallery_page_is_not_found_whatever_its_visibility()
    {
        await _db!.GetCollection<Gallery.Web.Data.GalleryDoc>("galleries").InsertOneAsync(new Gallery.Web.Data.GalleryDoc
        {
            Id = "hhhhhhhhhhhhhhhhhhhhhhhh22", SeedKey = "empty-priv", Slug = "draft", Title = "Draft",
            Visibility = "private", Items = [], Card = MakeCard("hhhhhhhhhhhhhhhhhhhhhhhh22", "draft", "Draft"),
            UpdatedAt = DateTime.UtcNow,
        });
        foreach (var path in new[] { $"/g/{GalleryEmptyId}/coming-soon", "/g/hhhhhhhhhhhhhhhhhhhhhhhh22/draft" })
        {
            var resp = await _client!.GetAsync(path);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, resp.StatusCode);
            Assert.Contains("Not found", await resp.Content.ReadAsStringAsync());
            Assert.Equal("noindex", resp.Headers.GetValues("X-Robots-Tag").Single());
        }
        // /g/{id} must not redirect into a page that 404s
        var shortResp = await _client!.GetAsync($"/g/{GalleryEmptyId}");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, shortResp.StatusCode);
    }

    // ── IV.12/IV.13/IV.17: every predictive fetch names what the destination will show ─────
    // The browser picks from <picture> by its FIRST supported <source> (AVIF), using that source's
    // srcset and sizes. A preload is reused only if it resolves to the same URL, so it must carry
    // the same srcset, the same sizes, and the same type. The owner saw a hover preload fetch the
    // WebP while the photo page loaded the AVIF: a second download on every click.

    private static (string Srcset, string Sizes) FirstSource(string html, string imageId)
    {
        var at = html.IndexOf($"id=\"p-{imageId}\"", StringComparison.Ordinal);
        Assert.True(at >= 0, $"picture p-{imageId} not found");
        var m = System.Text.RegularExpressions.Regex.Match(html[at..],
            "<source type=\"image/avif\" srcset=\"([^\"]+)\" sizes=\"([^\"]+)\"");
        Assert.True(m.Success, "no AVIF <source> on the destination picture");
        return (System.Net.WebUtility.HtmlDecode(m.Groups[1].Value), System.Net.WebUtility.HtmlDecode(m.Groups[2].Value));
    }

    private static string Attr(string html, string anchor, string name)
    {
        var at = html.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(at >= 0, $"{anchor} not found");
        var tagStart = html.LastIndexOf('<', at);
        var tag = html[tagStart..html.IndexOf('>', at)];
        var m = System.Text.RegularExpressions.Regex.Match(tag, $"{name}=\"([^\"]*)\"");
        Assert.True(m.Success, $"{name} missing on {anchor}");
        return System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    [Fact]
    public async Task Hover_preload_names_the_same_candidates_the_photo_page_shows()
    {
        var gallery = await _client!.GetStringAsync($"/g/{GalleryPubId1}/autumn-walk");
        var photo = await _client!.GetStringAsync($"/g/{GalleryPubId1}/autumn-walk/{ImgId2}");
        var anchor = $"href=\"/g/{GalleryPubId1}/autumn-walk/{ImgId2}\"";

        var (srcset, sizes) = FirstSource(photo, ImgId2);
        Assert.Equal(srcset, Attr(gallery, anchor, "data-preload-srcset"));
        Assert.Equal(sizes, Attr(gallery, anchor, "data-preload-sizes"));
        Assert.Contains("type: 'image/avif'", gallery); // the loader's detached <source> is typed like the page's
    }

    [Fact]
    public async Task Neighbour_prefetch_names_the_candidates_the_neighbour_pages_show()
    {
        var middle = await _client!.GetStringAsync($"/g/{GalleryPubId1}/autumn-walk/{ImgId2}");
        var prev = await _client!.GetStringAsync($"/g/{GalleryPubId1}/autumn-walk/{ImgId1}");
        var next = await _client!.GetStringAsync($"/g/{GalleryPubId1}/autumn-walk/{ImgId3}");

        // One srcset per neighbour; srcsets contain spaces and commas, so they are joined with "|".
        var expected = string.Join("|", FirstSource(prev, ImgId1).Srcset, FirstSource(next, ImgId3).Srcset);
        Assert.Equal(expected, Attr(middle, "<article class=\"solo\"", "data-prefetch"));
        Assert.Equal("100vw", FirstSource(prev, ImgId1).Sizes); // the loader preloads neighbours at 100vw
    }

    [Fact]
    public async Task Link_header_preloads_the_primary_image_the_page_shows()
    {
        var resp = await _client!.GetAsync($"/g/{GalleryPubId1}/autumn-walk/{ImgId2}");
        var html = await resp.Content.ReadAsStringAsync();
        var link = string.Join(",", resp.Headers.GetValues("Link"));
        var (srcset, sizes) = FirstSource(html, ImgId2);

        Assert.Contains($"imagesrcset=\"{srcset}\"", link);
        Assert.Contains($"imagesizes=\"{sizes}\"", link);
        Assert.Contains("type=\"image/avif\"", link);
        Assert.DoesNotContain(".webp", link);
    }
}

using Gallery.Core.Content;
using Gallery.Core.Ids;
using Gallery.Core.Images;
using Gallery.Web.Components.Molecules;
using Gallery.Web.Components.Organisms;
using Gallery.Web.Data;
using Gallery.Web.Site.Pages;
using Gallery.Web.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.OutputCaching;

namespace Gallery.Web.Site;

/// <summary>
/// Maps public routes: / (home), /g (listing), /g/{id}, /g/{id}/{slug}, /g/{id}/{slug}/{imageId},
/// /c (013: cameras) and /c/{slug} (013: one camera).
/// GET and HEAD only. Replaces the Hello World stub from AppBuilder.
/// </summary>
public static partial class PublicEndpoints
{
    public static void MapPublic(this WebApplication app)
    {
        var siteOptions = app.Services.GetRequiredService<SiteOptions>();
        string[] verbs = ["GET", "HEAD"];

        // Home (011, contracts/routes.md): the links page. It reads nothing, so it takes no ContentReader.
        var home = app.MapMethods("/", verbs, () =>
            (IResult)new StatusAndHeadersResult(200,
                headers: new() { ["Cache-Control"] = PublicCache.HtmlPolicy },
                inner: new RazorComponentResult<HomePage>(new Dictionary<string, object?>()),
                siteOptions.OutputCache ? PublicCache.PolicyName : null));

        // Listing (011: moved from "/")
        var listing = app.MapMethods("/g", verbs, async (HttpContext http, ContentReader reader, StorageOptions storage) =>
        {
            // R-03: routing delivers "/g/" to this endpoint (a separate "/g/" route is ambiguous with
            // it), so the one canonical address is enforced here, before the read.
            if (http.Request.Path.Value == "/g/")
                return (IResult)new ShortPolicyRedirectResult(Trails.ListingHref);

            try
            {
                var site = await reader.HomeAsync();
                var url = MakeUrl(storage);
                // II.6: never list an empty gallery. The seed already keeps them out of homeCards;
                // filtering here too means a stale listing cannot show one either.
                var cards = site?.HomeCards
                    .Where(c => c.Cover is not null)
                    .Select(c => ViewMapper.ToCard(c, url))
                    .ToList() ?? [];

                IResult result = new RazorComponentResult<ListingPage>(new Dictionary<string, object?>
                {
                    [nameof(ListingPage.Cards)] = (IReadOnlyList<ContactSheetItem>)cards
                });

                return ApplyListingHeaders(result, cards, siteOptions);
            }
            catch (Exception ex) when (IsDataException(ex))
            {
                return TypedResults.Text("Something went wrong", statusCode: 500);
            }
        });

        // Gallery short redirect: /g/{id} → /g/{id}/{slug}
        app.MapMethods("/g/{id}", verbs, async (string id, ContentReader reader) =>
        {
            if (!OpaqueId.IsValid(id))
                return NotFoundResult();

            try
            {
                var gallery = await reader.GalleryAsync(id, forPhoto: false);
                if (gallery is null || gallery.Items.Count == 0) // II.6: an empty gallery is not shown
                    return NotFoundResult();

                return (IResult)new ShortPolicyRedirectResult($"/g/{id}/{gallery.Slug}");
            }
            catch (Exception ex) when (IsDataException(ex))
            {
                return TypedResults.Text("Something went wrong", statusCode: 500);
            }
        });

        // Gallery page
        var galleryPage = app.MapMethods("/g/{id}/{slug}", verbs, async (string id, string slug, ContentReader reader, StorageOptions storage) =>
        {
            if (!OpaqueId.IsValid(id))
                return NotFoundResult();

            try
            {
                var gallery = await reader.GalleryAsync(id, forPhoto: false);
                if (gallery is null || gallery.Items.Count == 0) // II.6: an empty gallery is not shown
                    return NotFoundResult();

                if (gallery.Slug != slug)
                    return (IResult)new ShortPolicyRedirectResult($"/g/{id}/{gallery.Slug}");

                var url = MakeUrl(storage);
                var imageDocs = await reader.ImagesAsync(gallery.Items, full: false);

                var noIndex = gallery.Visibility != "public";
                var items = imageDocs.Select(img => GridTile(img, url, $"/g/{id}/{slug}/{img.Id}")).ToList();

                // 010 P2 [SAFETY] II.2: neighbour links only on a public gallery with a stored nav.
                // Whatever a non-public document holds, its page links to no other gallery.
                var nav = gallery.Visibility == "public" ? gallery.Nav : null;

                IResult result = new RazorComponentResult<GalleryPage>(new Dictionary<string, object?>
                {
                    [nameof(GalleryPage.Title)] = gallery.Title,
                    [nameof(GalleryPage.Description)] = gallery.Description,
                    [nameof(GalleryPage.NoIndex)] = (bool)noIndex,
                    [nameof(GalleryPage.Items)] = (IReadOnlyList<ContactSheetItem>)items,
                    [nameof(GalleryPage.Prev)] = Neighbour(nav?.Prev),
                    [nameof(GalleryPage.Next)] = Neighbour(nav?.Next),
                    // 011 (FR-014): links only to "/" and "/g", so a private gallery's page names no other gallery.
                    [nameof(GalleryPage.Crumbs)] = Trails.Under(new Crumb(gallery.Title)),
                });

                return ApplyGalleryHeaders(result, items, noIndex, siteOptions);
            }
            catch (Exception ex) when (IsDataException(ex))
            {
                return TypedResults.Text("Something went wrong", statusCode: 500);
            }
        });

        // Photo page
        var photoPage = app.MapMethods("/g/{id}/{slug}/{imageId}", verbs, async (string id, string slug, string imageId, ContentReader reader, StorageOptions storage) =>
        {
            if (!OpaqueId.IsValid(id) || !OpaqueId.IsValid(imageId))
                return NotFoundResult();

            try
            {
                var gallery = await reader.GalleryAsync(id, forPhoto: true);
                if (gallery is null)
                    return NotFoundResult();

                if (gallery.Slug != slug)
                    return (IResult)new ShortPolicyRedirectResult($"/g/{id}/{gallery.Slug}/{imageId}");

                if (!gallery.Items.Contains(imageId))
                    return NotFoundResult();

                var (prevId, nextId) = GalleryNav.Neighbours(gallery.Items, imageId);
                var idsToFetch = new[] { prevId, imageId, nextId }
                    .Where(x => x is not null)
                    .Cast<string>()
                    .Distinct()
                    .ToList();

                var imageDocs = await reader.ImagesAsync(idsToFetch, full: true);
                var imageIndex = imageDocs.ToDictionary(d => d.Id);

                if (!imageIndex.TryGetValue(imageId, out var currentDoc))
                    return NotFoundResult();

                var url = MakeUrl(storage);
                var currentView = ViewMapper.ToView(currentDoc, url);
                var noIndex = gallery.Visibility != "public";

                string? prevHref = prevId is not null ? $"/g/{id}/{slug}/{prevId}" : null;
                string? nextHref = nextId is not null ? $"/g/{id}/{slug}/{nextId}" : null;

                // PrefetchUrls: each neighbour's AVIF srcset, the candidates its photo page's first
                // <source> offers at sizes=100vw, so the loader's preload is the exact file that page
                // uses. Srcsets contain spaces and commas, hence the "|" separator. Missing neighbours
                // are omitted.
                var prefetchParts = new List<string>();
                if (prevId is not null && imageIndex.TryGetValue(prevId, out var prevDoc))
                    prefetchParts.Add(SrcSet.Avif(ViewMapper.ToView(prevDoc, url)));
                if (nextId is not null && imageIndex.TryGetValue(nextId, out var nextDoc))
                    prefetchParts.Add(SrcSet.Avif(ViewMapper.ToView(nextDoc, url)));
                var prefetchUrls = prefetchParts.Count > 0 ? string.Join("|", prefetchParts) : null;

                IResult result = new RazorComponentResult<PhotoPage>(new Dictionary<string, object?>
                {
                    [nameof(PhotoPage.Image)] = currentView,
                    [nameof(PhotoPage.Caption)] = currentDoc.Caption,
                    [nameof(PhotoPage.PrevHref)] = prevHref,
                    [nameof(PhotoPage.NextHref)] = nextHref,
                    [nameof(PhotoPage.PrefetchUrls)] = prefetchUrls,
                    [nameof(PhotoPage.NoIndex)] = (bool)noIndex,
                    [nameof(PhotoPage.GalleryTitle)] = gallery.Title,
                    // 010 B2/B3: the gallery crumb lands on this photo's tile; its position (no new read).
                    [nameof(PhotoPage.GalleryHref)] = $"/g/{id}/{slug}#p-{imageId}",
                    [nameof(PhotoPage.Index)] = gallery.Items.IndexOf(imageId) + 1,
                    [nameof(PhotoPage.Total)] = gallery.Items.Count,
                });

                return ApplyPhotoHeaders(result, currentView, noIndex, siteOptions);
            }
            catch (Exception ex) when (IsDataException(ex))
            {
                return TypedResults.Text("Something went wrong", statusCode: 500);
            }
        });

        // 013 C1 (contracts/routes.md): the camera listing. Mirrors /g: one read (db.cameras.list).
        var cameras = app.MapMethods("/c", verbs, async (HttpContext http, ContentReader reader, StorageOptions storage) =>
        {
            // 011 R-03 pattern: routing delivers "/c/" here; a separate "/c/" route is ambiguous.
            if (http.Request.Path.Value == "/c/")
                return (IResult)new ShortPolicyRedirectResult(Trails.CamerasHref);

            try
            {
                var docs = await reader.CamerasAsync();
                var url = MakeUrl(storage);
                var cards = docs.Select(c => ViewMapper.ToCameraCard(c.Card, url)).ToList();

                IResult result = new RazorComponentResult<CameraListingPage>(new Dictionary<string, object?>
                {
                    [nameof(CameraListingPage.Cards)] = (IReadOnlyList<ContactSheetItem>)cards
                });

                return ApplyCameraListingHeaders(result, cards, siteOptions);
            }
            catch (Exception ex) when (IsDataException(ex))
            {
                return TypedResults.Text("Something went wrong", statusCode: 500);
            }
        });

        // 013 C2 (contracts/routes.md): one camera's grid. Mirrors the gallery page: two reads.
        var cameraPage = app.MapMethods("/c/{slug}", verbs, async (string slug, ContentReader reader, StorageOptions storage) =>
        {
            // [SAFETY] FR-009: junk never reaches the reader, and every refusal is the same 404.
            if (!CameraSlugRx().IsMatch(slug))
                return NotFoundResult();

            try
            {
                var camera = await reader.CameraAsync(slug);
                if (camera is null || camera.Items.Count == 0)
                    return NotFoundResult();

                var url = MakeUrl(storage);
                var imageDocs = await reader.ImagesAsync(camera.Items, full: false);

                // Research R-09 render-time guard (the grid projection omits deletingAt, so this
                // only drops a tombstone a fuller read would carry).
                var items = imageDocs.Where(img => img.DeletingAt is null)
                    .Select(img => GridTile(img, url, $"/c/{slug}/{img.Id}")).ToList();

                IResult result = new RazorComponentResult<CameraPage>(new Dictionary<string, object?>
                {
                    [nameof(CameraPage.Title)] = camera.Name,
                    [nameof(CameraPage.Items)] = (IReadOnlyList<ContactSheetItem>)items,
                    // FR-010: Home and By Camera only, so the page names no gallery.
                    [nameof(CameraPage.Crumbs)] = Trails.UnderCameras(new Crumb(camera.Name)),
                });

                return ApplyCameraHeaders(result, items, siteOptions);
            }
            catch (Exception ex) when (IsDataException(ex))
            {
                return TypedResults.Text("Something went wrong", statusCode: 500);
            }
        });

        // 009 T002 (research R-07): the 006 policy only applies to endpoints that carry it, so the
        // page routes (011: home and listing too) attach it explicitly. The /g/{id} short route is not cached, and the
        // policy's default rules cache only 200 responses (never a 301 or 404).
        if (siteOptions.OutputCache)
        {
            home.CacheOutput(PublicCache.PolicyName);
            listing.CacheOutput(PublicCache.PolicyName);
            galleryPage.CacheOutput(PublicCache.PolicyName);
            photoPage.CacheOutput(PublicCache.PolicyName);
            cameras.CacheOutput(PublicCache.PolicyName);
            cameraPage.CacheOutput(PublicCache.PolicyName);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>One photo tile of a grid page (gallery page and 013 camera page), linking to <paramref name="href"/>.</summary>
    private static ContactSheetItem GridTile(ImageDoc img, Func<string, string> url, string href)
    {
        var view = ViewMapper.ToView(img, url);
        return new ContactSheetItem(
            Image: view,
            Href: href,
            Title: view.Title,
            TransitionId: img.Id,
            PreloadSrcSet: SrcSet.Avif(view),
            PreloadSizes: "100vw"
        );
    }

    /// <summary>013 contracts/routes.md refusals: a camera slug as <c>Slug.From</c> writes it, plus a suffix.</summary>
    [System.Text.RegularExpressions.GeneratedRegex("^[a-z0-9-]{1,64}$")]
    private static partial System.Text.RegularExpressions.Regex CameraSlugRx();

    // Pages only build image URLs, from ImageBaseUrl. They must not resolve ImageStore: that builds
    // an S3 client, which throws when the storage credentials are absent, and research R-10 requires
    // pages to render without them (only the seed and storage tests need credentials).
    internal static Func<string, string> MakeUrl(StorageOptions storage)
    {
        var baseUrl = (storage.ImageBaseUrl ?? "").TrimEnd('/');
        return key => $"{baseUrl}/{key}";
    }

    private static GalleryNeighbour? Neighbour(GalleryLinkDoc? n) =>
        n is null ? null : new GalleryNeighbour($"/g/{n.Id}/{n.Slug}", n.Title);

    private static bool IsDataException(Exception ex) =>
        ex is MongoDB.Driver.MongoException || ex is TimeoutException;

    private static IResult NotFoundResult()
    {
        return new StatusAndHeadersResult(404,
            headers: new() { ["Cache-Control"] = PublicCache.ShortPolicy, ["X-Robots-Tag"] = "noindex" },
            inner: new RazorComponentResult<NotFoundPage>(new Dictionary<string, object?>()));
    }

    private static IResult ApplyListingHeaders(IResult inner, List<ContactSheetItem> cards, SiteOptions siteOptions)
    {
        var headers = new Dictionary<string, string>
        {
            ["Cache-Control"] = PublicCache.HtmlPolicy
        };
        var first = cards.FirstOrDefault(c => c.Image is not null);
        if (first?.Image is not null)
            headers["Link"] = ViewMapper.PreloadLink(first.Image, Sizes.ForGrid(3, 3, 1));

        return new StatusAndHeadersResult(200, headers, inner, siteOptions.OutputCache ? PublicCache.PolicyName : null);
    }

    private static IResult ApplyGalleryHeaders(IResult inner, List<ContactSheetItem> items, bool noIndex, SiteOptions siteOptions)
    {
        var headers = new Dictionary<string, string>
        {
            ["Cache-Control"] = PublicCache.HtmlPolicy
        };
        if (noIndex) headers["X-Robots-Tag"] = "noindex";

        var first = items.FirstOrDefault(i => i.Image is not null);
        if (first?.Image is not null)
            headers["Link"] = ViewMapper.PreloadLink(first.Image, Sizes.ForGrid(4, 3, 1));

        return new StatusAndHeadersResult(200, headers, inner, siteOptions.OutputCache ? PublicCache.PolicyName : null);
    }

    // 013 C1/C2: modelled on ApplyListingHeaders/ApplyGalleryHeaders. Never X-Robots-Tag: every camera
    // page shows only visible photos, so it is indexable like /g (contracts/routes.md "Headers").
    private static IResult ApplyCameraListingHeaders(IResult inner, List<ContactSheetItem> cards, SiteOptions siteOptions) =>
        CameraGridHeaders(inner, cards, Sizes.ForGrid(3, 3, 1), siteOptions);

    private static IResult ApplyCameraHeaders(IResult inner, List<ContactSheetItem> items, SiteOptions siteOptions) =>
        CameraGridHeaders(inner, items, Sizes.ForGrid(4, 3, 1), siteOptions);

    private static IResult CameraGridHeaders(IResult inner, List<ContactSheetItem> items, string sizes, SiteOptions siteOptions)
    {
        var headers = new Dictionary<string, string>
        {
            ["Cache-Control"] = PublicCache.HtmlPolicy
        };
        var first = items.FirstOrDefault(i => i.Image is not null);
        if (first?.Image is not null)
            headers["Link"] = ViewMapper.PreloadLink(first.Image, sizes);

        return new StatusAndHeadersResult(200, headers, inner, siteOptions.OutputCache ? PublicCache.PolicyName : null);
    }

    private static IResult ApplyPhotoHeaders(IResult inner, ImageView image, bool noIndex, SiteOptions siteOptions)
    {
        var headers = new Dictionary<string, string>
        {
            ["Cache-Control"] = PublicCache.HtmlPolicy,
            ["Link"] = ViewMapper.PreloadLink(image, "100vw")
        };
        if (noIndex) headers["X-Robots-Tag"] = "noindex";

        return new StatusAndHeadersResult(200, headers, inner, siteOptions.OutputCache ? PublicCache.PolicyName : null);
    }
}

/// <summary>301 redirect with ShortPolicy Cache-Control.</summary>
file sealed class ShortPolicyRedirectResult(string location) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.Headers["Cache-Control"] = Gallery.Web.Site.PublicCache.ShortPolicy;
        httpContext.Response.Headers["Location"] = location;
        httpContext.Response.StatusCode = 301;
        return Task.CompletedTask;
    }
}

/// <summary>Wraps an inner IResult to apply response headers and status code.</summary>
file sealed class StatusAndHeadersResult(
    int? statusCode,
    Dictionary<string, string> headers,
    IResult inner,
    string? cachePolicyName = null) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        foreach (var (k, v) in headers)
            httpContext.Response.Headers[k] = v;

        if (statusCode.HasValue && statusCode != 200)
            httpContext.Response.StatusCode = statusCode.Value;

        if (cachePolicyName is not null)
        {
            var cacheFeature = httpContext.Features.Get<IOutputCacheFeature>();
            if (cacheFeature is not null)
            {
                cacheFeature.Context.Tags.Add(cachePolicyName);
                cacheFeature.Context.Tags.Add(Gallery.Web.Site.PublicCache.PageTag(httpContext.Request.Path));
            }
        }

        await inner.ExecuteAsync(httpContext);
    }
}

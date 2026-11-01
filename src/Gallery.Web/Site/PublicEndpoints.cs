using Gallery.Core.Content;
using Gallery.Core.Ids;
using Gallery.Core.Images;
using Gallery.Web.Components.Organisms;
using Gallery.Web.Data;
using Gallery.Web.Site.Pages;
using Gallery.Web.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.OutputCaching;

namespace Gallery.Web.Site;

/// <summary>
/// Maps public routes: /, /g/{id}, /g/{id}/{slug}, /g/{id}/{slug}/{imageId}.
/// GET and HEAD only. Replaces the Hello World stub from AppBuilder.
/// </summary>
public static class PublicEndpoints
{
    public static void MapPublic(this WebApplication app)
    {
        var siteOptions = app.Services.GetRequiredService<SiteOptions>();
        string[] verbs = ["GET", "HEAD"];

        // Home
        app.MapMethods("/", verbs, async (ContentReader reader, StorageOptions storage) =>
        {
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

                IResult result = new RazorComponentResult<HomePage>(new Dictionary<string, object?>
                {
                    [nameof(HomePage.Cards)] = (IReadOnlyList<ContactSheetItem>)cards
                });

                return ApplyHomeHeaders(result, cards, siteOptions);
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
        app.MapMethods("/g/{id}/{slug}", verbs, async (string id, string slug, ContentReader reader, StorageOptions storage) =>
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
                var items = imageDocs.Select(img =>
                {
                    var view = ViewMapper.ToView(img, url);
                    return new ContactSheetItem(
                        Image: view,
                        Href: $"/g/{id}/{slug}/{img.Id}",
                        Title: view.Title,
                        TransitionId: img.Id,
                        PreloadSrcSet: SrcSet.Avif(view),
                        PreloadSizes: "100vw"
                    );
                }).ToList();

                IResult result = new RazorComponentResult<GalleryPage>(new Dictionary<string, object?>
                {
                    [nameof(GalleryPage.Title)] = gallery.Title,
                    [nameof(GalleryPage.Description)] = gallery.Description,
                    [nameof(GalleryPage.NoIndex)] = (bool)noIndex,
                    [nameof(GalleryPage.Items)] = (IReadOnlyList<ContactSheetItem>)items
                });

                return ApplyGalleryHeaders(result, items, noIndex, siteOptions);
            }
            catch (Exception ex) when (IsDataException(ex))
            {
                return TypedResults.Text("Something went wrong", statusCode: 500);
            }
        });

        // Photo page
        app.MapMethods("/g/{id}/{slug}/{imageId}", verbs, async (string id, string slug, string imageId, ContentReader reader, StorageOptions storage) =>
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
                });

                return ApplyPhotoHeaders(result, currentView, noIndex, siteOptions);
            }
            catch (Exception ex) when (IsDataException(ex))
            {
                return TypedResults.Text("Something went wrong", statusCode: 500);
            }
        });
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    // Pages only build image URLs, from ImageBaseUrl. They must not resolve ImageStore: that builds
    // an S3 client, which throws when the storage credentials are absent, and research R-10 requires
    // pages to render without them (only the seed and storage tests need credentials).
    internal static Func<string, string> MakeUrl(StorageOptions storage)
    {
        var baseUrl = (storage.ImageBaseUrl ?? "").TrimEnd('/');
        return key => $"{baseUrl}/{key}";
    }

    private static bool IsDataException(Exception ex) =>
        ex is MongoDB.Driver.MongoException || ex is TimeoutException;

    private static IResult NotFoundResult()
    {
        return new StatusAndHeadersResult(404,
            headers: new() { ["Cache-Control"] = PublicCache.ShortPolicy, ["X-Robots-Tag"] = "noindex" },
            inner: new RazorComponentResult<NotFoundPage>(new Dictionary<string, object?>()));
    }

    private static IResult ApplyHomeHeaders(IResult inner, List<ContactSheetItem> cards, SiteOptions siteOptions)
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
                cacheFeature.Context.Tags.Add(cachePolicyName);
        }

        await inner.ExecuteAsync(httpContext);
    }
}

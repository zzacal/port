using Gallery.Web.Observability;
using Gallery.Web.Site;
using Microsoft.AspNetCore.OutputCaching;

namespace Gallery.Web.Library;

/// <summary>
/// Evicts the local origin output cache for each changed public page (FR-044, research R-07). It
/// takes page PATHS (no host), e.g. <c>/g/{gid}/{slug}</c>, and evicts the <c>page:{path}</c> tag
/// that <see cref="PublicCache.PageTag"/> puts on every cached public response. In-memory and fast,
/// so it runs inside the owner's request. Registered by <c>EdgeSetup.AddEdge</c> (tasks.md T019).
/// </summary>
public sealed class PageCacheEvictor(IOutputCacheStore store, OpTimer timer)
{
    public Task EvictAsync(IEnumerable<string> pagePaths, CancellationToken ct = default) =>
        timer.Time("cache.evict", async () =>
        {
            foreach (var path in pagePaths.Distinct(StringComparer.Ordinal))
                await store.EvictByTagAsync(PublicCache.PageTag(path), ct);
            return true;
        });
}

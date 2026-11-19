using System.Net;
using System.Text.RegularExpressions;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T041 [SAFETY] (Constitution II.6; SC-006, FR-030; contracts/http.md G4): an empty gallery can
/// never be published over HTTP, whatever the request carries, through G4 (the list's switch) or B2
/// (the builder's Save with the switch on). The emptiness check runs FIRST: a publish (with or
/// without the retired confirm=1), a form with no fields and a bodiless POST to G4, and a forged
/// public=on save (with typed text) to B2 all answer 409
/// with the exact reason in <c>role="status"</c>, store nothing (visibility stays <c>private</c>, the
/// <c>galleries</c>/<c>site</c> documents are unchanged), leave <c>/</c> byte-identical and make no
/// edge call. The race (the last photo removed in another tab between the load and the write) is
/// closed by the conditional write, driven here through the service's <c>beforePublishWrite</c>
/// seam on the real HTTP route. Both owner surfaces render the empty gallery's switch disabled with
/// the reason. Do not weaken: a failure here means a gallery with no photos can reach the home page.
/// </summary>
[Collection("s3")]
public class PublishRefusalTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private const string Reason = "Add a photo before making this gallery public";
    private static KeyValuePair<string, string> F(string k, string v) => GalleryHtml.F(k, v);

    /// <summary>The JSON answer's root element (020 TI-15).</summary>
    private static async Task<System.Text.Json.JsonElement> JsonAsync(HttpResponseMessage resp)
    {
        using var doc = await UploadHost.ParseAsync(resp);
        Assert.NotNull(doc);
        return doc!.RootElement.Clone();
    }

    private static async Task<string> DumpAsync(LibraryHost h)
    {
        var parts = new List<string>();
        foreach (var name in new[] { "galleries", "site" })
        {
            var docs = await h.Db.GetCollection<BsonDocument>(name).Find(FilterDefinition<BsonDocument>.Empty)
                .Sort(Builders<BsonDocument>.Sort.Ascending("_id")).ToListAsync();
            parts.Add(name + ":" + string.Join("\n", docs.Select(d => d.ToJson())));
        }
        return string.Join("\n---\n", parts);
    }

    private static async Task AssertRefusedAsync(LibraryHost h, HttpResponseMessage resp, string gid, string home, string dump, string what)
    {
        Assert.True(resp.StatusCode == HttpStatusCode.Conflict, $"{what}: answered {(int)resp.StatusCode}, expected 409");
        Assert.Equal(Reason, (await JsonAsync(resp)).GetProperty("message").GetString());
        Assert.Equal("private", (await GalleryHtml.LoadAsync(h, gid)).Visibility);
        await h.EdgeIdleAsync();
        Assert.Equal(0, h.Recorder.CallCount);
        Assert.Equal(dump, await DumpAsync(h));
        Assert.Equal(home, await GalleryHtml.HomeRawAsync(h));
    }

    [Fact]
    public async Task An_empty_gallery_is_refused_409_for_every_request_shape_and_nothing_changes()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        // A listed gallery so the listing "/g" is not trivially empty.
        var ids = await GalleryHtml.ImageIdsAsync(h, 1);
        await GalleryHtml.GalleryAsync(h, "Listed", ids, publish: true);
        var gid = await GalleryHtml.GalleryAsync(h, "Empty", []);
        await h.SettleAsync();
        h.Recorder.Clear();

        var home = await GalleryHtml.HomeRawAsync(h);
        Assert.Contains("/listed", home);
        var dump = await DumpAsync(h);
        var path = $"/admin/api/galleries/{gid}/visibility";

        // 020 TI-15: the three G4 publish shapes are one A6 request; the old form with no fields is A6 { public: false }.
        await AssertRefusedAsync(h, await h.PostJsonAsync(path, new { @public = true }),
            gid, home, dump, "A6 { public: true }");
        await AssertRefusedAsync(h, await h.PostJsonAsync(path, new { @public = false }),
            gid, home, dump, "A6 { public: false } on the empty private gallery");

        // A bodiless POST with only the CSRF header parses nothing: 400 bad-request, and nothing changes.
        var bare = await h.PostBareAsync(path, csrfHeader: true);
        Assert.Equal(HttpStatusCode.BadRequest, bare.StatusCode);
        Assert.Equal("bad-request", (await JsonAsync(bare)).GetProperty("error").GetString());
        Assert.Equal("private", (await GalleryHtml.LoadAsync(h, gid)).Visibility);
        Assert.Equal(dump, await DumpAsync(h));
        Assert.Equal(home, await GalleryHtml.HomeRawAsync(h));

        // The builder's forged public=on saves are A6 { public: true } (A5 is not sent: text is not the subject).
        await AssertRefusedAsync(h, await h.PostJsonAsync(path, new { @public = true }),
            gid, home, dump, "the builder's forged public=on save, now A6 { public: true }");

        // A bodiless POST with no token at all never reaches the handler (antiforgery, 400) and stores nothing either.
        var noToken = await h.PostBareAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.Equal("private", (await GalleryHtml.LoadAsync(h, gid)).Visibility);
        Assert.Equal(dump, await DumpAsync(h));
        Assert.Equal(home, await GalleryHtml.HomeRawAsync(h));
        await h.EdgeIdleAsync();
        Assert.Equal(0, h.Recorder.CallCount);
        Assert.DoesNotContain(h.LogLines, l => l.StartsWith($"owner.action gallery.publish {gid} ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_remove_racing_the_publish_is_refused_by_the_conditional_write()
    {
        var fired = 0;
        string? photo = null;
        LibraryHost? host = null;
        // The seam empties the gallery in "another tab" (its own scope) between the load and the write.
        Func<string, Task> emptyIt = async gid =>
        {
            if (Interlocked.Exchange(ref fired, 1) == 1) return;
            using var other = host!.Scope();
            var r = await other.ServiceProvider.GetRequiredService<GalleryService>().RemoveAsync(gid, photo!);
            Assert.Equal(1, r.Removed);
        };
        // AppBuilder runs `configure` BEFORE AddLibrary registers GalleryService, so a plain
        // re-registration there would be overridden. The swap happens when the container is built,
        // after every registration: the real G4 route then resolves the seamed service.
        await using var h = await LibraryHost.CreateAsync(mongo, s3, configure: b =>
            b.Host.UseServiceProviderFactory(new SwapAtBuild(services =>
            {
                services.RemoveAll<GalleryService>();
                services.AddScoped(sp => ActivatorUtilities.CreateInstance<GalleryService>(sp, emptyIt));
            })));
        host = h;
        var ids = await GalleryHtml.ImageIdsAsync(h, 1);
        photo = ids[0];
        var gid = await GalleryHtml.GalleryAsync(h, "Race", ids);
        await h.SettleAsync();
        var home = await GalleryHtml.HomeRawAsync(h);

        var resp = await h.PostJsonAsync($"/admin/api/galleries/{gid}/visibility", new { @public = true });

        Assert.Equal(1, fired);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.Equal(Reason, (await JsonAsync(resp)).GetProperty("message").GetString());
        var g = await GalleryHtml.LoadAsync(h, gid);
        Assert.Equal("private", g.Visibility);
        Assert.Empty(g.Items);
        Assert.Empty((await h.SiteAsync()).HomeCards);
        Assert.Equal(home, await GalleryHtml.HomeRawAsync(h));
        Assert.DoesNotContain(h.LogLines, l => l.StartsWith($"owner.action gallery.publish {gid} ", StringComparison.Ordinal));
        await InvariantChecker.AssertAsync(h.Db);
    }

    /// <summary>The default container, with <paramref name="edit"/> applied to the final service collection.</summary>
    private sealed class SwapAtBuild(Action<IServiceCollection> edit) : IServiceProviderFactory<IServiceCollection>
    {
        private readonly DefaultServiceProviderFactory _inner = new();

        public IServiceCollection CreateBuilder(IServiceCollection services)
        {
            edit(services);
            return _inner.CreateBuilder(services);
        }

        public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder) =>
            _inner.CreateServiceProvider(containerBuilder);
    }
}

using System.Net;
using System.Text;
using Amazon.S3.Model;
using Gallery.Web.Storage;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gallery.Web.Tests;

/// <summary>
/// FR-022: the same storage tests run against the S3Proxy emulator (every run) and real dev R2
/// (on demand, GALLERY_R2_TESTS=1). The presign tests are III.9 safety controls: a URL presigned
/// for PUT must refuse GET and must not authorise a different key.
/// </summary>
public abstract class StorageTests
{
    private const string Immutable = "public, max-age=31536000, immutable";

    protected abstract StorageOptions Options { get; }
    protected abstract bool Enabled { get; }
    protected abstract string Prefix { get; }

    private ImageStore NewStore() => new(new S3ClientFactory(Options), NullLogger<ImageStore>.Instance);
    private string Bucket => Options.PublicBucket!;
    private string Key(string name) => $"{Prefix}{Guid.NewGuid():N}-{name}";

    private static MemoryStream Bytes(string s) => new(Encoding.ASCII.GetBytes(s));

    private async Task<GetObjectMetadataResponse> Head(string key)
        => await new S3ClientFactory(Options).CreateClient().GetObjectMetadataAsync(Bucket, key);

    protected async Task Put_then_head_has_immutable_cache_control()
    {
        if (!Enabled) return;
        var store = NewStore();
        var key = Key("a.webp");
        try
        {
            await store.PutAsync(Bucket, key, Bytes("hello"), "image/webp", Immutable);
            Assert.True(await store.ExistsAsync(Bucket, key));
            Assert.Equal(Immutable, (await Head(key)).Headers.CacheControl);
        }
        finally { await store.DeleteAsync(Bucket, key); }
    }

    protected async Task Head_on_missing_key_is_false()
    {
        if (!Enabled) return;
        Assert.False(await NewStore().ExistsAsync(Bucket, Key("missing")));
    }

    protected async Task Ranged_get_returns_exactly_the_requested_bytes()
    {
        if (!Enabled) return;
        var store = NewStore();
        var key = Key("r.bin");
        try
        {
            await store.PutAsync(Bucket, key, Bytes("0123456789"), "application/octet-stream", Immutable);
            Assert.Equal("234", Encoding.ASCII.GetString(await store.GetRangeAsync(Bucket, key, 2, 4)));
        }
        finally { await store.DeleteAsync(Bucket, key); }
    }

    protected async Task Copy_with_replace_carries_the_new_cache_control()
    {
        if (!Enabled) return;
        var store = NewStore();
        var src = Key("src.webp");
        var dst = Key("dst.webp");
        try
        {
            await store.PutAsync(Bucket, src, Bytes("x"), "image/webp", "no-cache");
            await store.CopyAsync(Bucket, src, Bucket, dst, "image/webp", Immutable);
            Assert.Equal(Immutable, (await Head(dst)).Headers.CacheControl);
        }
        finally
        {
            await store.DeleteAsync(Bucket, src);
            await store.DeleteAsync(Bucket, dst);
        }
    }

    protected async Task Delete_removes_the_object()
    {
        if (!Enabled) return;
        var store = NewStore();
        var key = Key("d.webp");
        await store.PutAsync(Bucket, key, Bytes("x"), "image/webp", Immutable);
        await store.DeleteAsync(Bucket, key);
        Assert.False(await store.ExistsAsync(Bucket, key));
    }

    // [SAFETY] III.9: single method, single key.
    protected async Task Presigned_put_allows_put_only_for_its_own_key()
    {
        if (!Enabled) return;
        var store = NewStore();
        var key = Key("p.webp");
        var other = key + "-other";
        try
        {
            var url = await store.PresignPutAsync(Bucket, key, "image/webp", TimeSpan.FromMinutes(5));
            using var http = new HttpClient();

            var put = new HttpRequestMessage(HttpMethod.Put, url) { Content = new ByteArrayContent([1, 2, 3]) };
            put.Content.Headers.ContentType = new("image/webp");
            Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(put)).StatusCode);

            Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(url)).StatusCode);

            var tampered = url.Replace(key, other); // keys are URL-safe, so they appear verbatim in the path
            Assert.NotEqual(url, tampered);
            var put2 = new HttpRequestMessage(HttpMethod.Put, tampered) { Content = new ByteArrayContent([1]) };
            put2.Content.Headers.ContentType = new("image/webp");
            Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(put2)).StatusCode);
            Assert.False(await store.ExistsAsync(Bucket, other));
        }
        finally
        {
            await store.DeleteAsync(Bucket, key);
            await store.DeleteAsync(Bucket, other);
        }
    }

    // [SAFETY] III.9: presigned URLs live at most 15 minutes.
    protected async Task Presign_over_15_minutes_throws()
    {
        if (!Enabled) return;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            NewStore().PresignPutAsync(Bucket, Key("x"), "image/webp", TimeSpan.FromMinutes(16)));
    }
}

[Collection("s3")]
public sealed class EmulatorStorageTests(S3ProxyFixture fx) : StorageTests
{
    protected override StorageOptions Options => fx.StorageOptions;
    protected override bool Enabled => true;
    protected override string Prefix => "test/";

    [Fact] public Task Put_then_head() => Put_then_head_has_immutable_cache_control();
    [Fact] public Task Head_missing() => Head_on_missing_key_is_false();
    [Fact] public Task Ranged_get() => Ranged_get_returns_exactly_the_requested_bytes();
    [Fact] public Task Copy_replace() => Copy_with_replace_carries_the_new_cache_control();
    [Fact] public Task Delete() => Delete_removes_the_object();
    [Fact] public Task Presign_put_only() => Presigned_put_allows_put_only_for_its_own_key();
    [Fact] public Task Presign_max_15_min() => Presign_over_15_minutes_throws();
}

[Trait("Category", "R2")]
public sealed class R2StorageTests : StorageTests, IClassFixture<R2Fixture>
{
    private readonly R2Fixture _fx;
    private readonly string _prefix = $"test/{Guid.NewGuid():N}/";

    public R2StorageTests(R2Fixture fx) => _fx = fx;

    protected override StorageOptions Options => _fx.StorageOptions;
    protected override bool Enabled => _fx.Enabled;
    protected override string Prefix => _prefix;

    [Fact] public Task Put_then_head_R2() => Put_then_head_has_immutable_cache_control();
    [Fact] public Task Head_missing_R2() => Head_on_missing_key_is_false();
    [Fact] public Task Ranged_get_R2() => Ranged_get_returns_exactly_the_requested_bytes();
    [Fact] public Task Copy_replace_R2() => Copy_with_replace_carries_the_new_cache_control();
    [Fact] public Task Delete_R2() => Delete_removes_the_object();
    [Fact] public Task Presign_put_only_R2() => Presigned_put_allows_put_only_for_its_own_key();
    [Fact] public Task Presign_max_15_min_R2() => Presign_over_15_minutes_throws();
}

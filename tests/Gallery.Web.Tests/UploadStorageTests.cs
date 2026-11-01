using System.Net;
using System.Text;
using Gallery.Web.Storage;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gallery.Web.Tests;

/// <summary>
/// 008 storage facts (T015–T017), run against the S3Proxy emulator (every run) and real dev R2
/// (on demand, GALLERY_R2_TESTS=1). Same abstract/emulator/R2 pattern as StorageTests, in a new
/// file so StorageTests.cs stays unmodified (decisions row 37).
/// [SAFETY] FR-044 (signed headers) and FR-042/SC-011 (presigned URL misuse) are blocking controls.
/// </summary>
public abstract class UploadStorageTests
{
    private const string Immutable = "public, max-age=31536000, immutable";

    protected abstract StorageOptions Options { get; }
    protected abstract bool Enabled { get; }
    protected abstract string Prefix { get; }

    private ImageStore NewStore() => new(new S3ClientFactory(Options), NullLogger<ImageStore>.Instance);
    private string Priv => Options.PrivateBucket!;
    private string Pub => Options.PublicBucket!;
    private string Key(string name) => $"{Prefix}{Guid.NewGuid():N}-{name}";

    private static MemoryStream Bytes(string s) => new(Encoding.ASCII.GetBytes(s));

    private static Dictionary<string, string> Query(string url)
    {
        var query = new Uri(url).Query.TrimStart('?');
        return query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]),
                          p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "",
                          StringComparer.Ordinal);
    }

    /// <summary>A PUT that sends exactly one request header of its own, Content-Type (as the browser does).</summary>
    private static async Task<HttpStatusCode> PutAsync(string url, byte[] body, string contentType)
    {
        using var http = new HttpClient();
        using var put = new HttpRequestMessage(HttpMethod.Put, url) { Content = new ByteArrayContent(body) };
        put.Content.Headers.ContentType = new(contentType);
        return (await http.SendAsync(put)).StatusCode;
    }

    private async Task Cleanup(ImageStore store, string bucket, params string[] keys)
    {
        foreach (var k in keys)
            await ImageStore.TryDeleteAsync(() => store.DeleteAsync(bucket, k), k, NullLogger.Instance);
    }

    // ── T015 [SAFETY] FR-044: the presigner signs content-type and host only ────────────────────

    protected async Task Presign_signs_content_type_and_host_only()
    {
        if (!Enabled) return;
        var store = NewStore();
        var key = Key("master.jpg");
        try
        {
            var url = await store.PresignPutAsync(Priv, key, "image/jpeg", TimeSpan.FromMinutes(15));
            var q = Query(url);

            Assert.Equal("content-type;host", q["X-Amz-SignedHeaders"]);
            Assert.True(int.Parse(q["X-Amz-Expires"]) <= 900, $"X-Amz-Expires={q["X-Amz-Expires"]}");
            Assert.DoesNotContain(q.Keys, k =>
                k.StartsWith("x-amz-checksum", StringComparison.OrdinalIgnoreCase) ||
                k.StartsWith("x-amz-sdk-checksum", StringComparison.OrdinalIgnoreCase) ||
                k.StartsWith("x-amz-meta-", StringComparison.OrdinalIgnoreCase));

            Assert.Equal(HttpStatusCode.OK, await PutAsync(url, [1, 2, 3], "image/jpeg"));
            var head = await store.HeadAsync(Priv, key);
            Assert.True(head.exists);
            Assert.Equal(3, head.length);
        }
        finally { await Cleanup(store, Priv, key); }
    }

    // ── T016 [SAFETY] FR-042 / SC-011: a presigned PUT URL refuses every other use ──────────────

    protected async Task Get_on_put_url_is_403_and_object_unchanged()
    {
        if (!Enabled) return;
        var store = NewStore();
        var key = Key("640.webp");
        try
        {
            var url = await store.PresignPutAsync(Priv, key, "image/webp", TimeSpan.FromMinutes(5));
            Assert.Equal(HttpStatusCode.OK, await PutAsync(url, [1, 2, 3], "image/webp"));
            var before = await store.HeadAsync(Priv, key);

            using var http = new HttpClient();
            Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(url)).StatusCode);

            var after = await store.HeadAsync(Priv, key);
            Assert.True(after.exists);
            Assert.Equal(before.etag, after.etag);
            Assert.Equal(3, after.length);
        }
        finally { await Cleanup(store, Priv, key); }
    }

    protected async Task Put_to_other_key_is_403_and_other_key_absent()
    {
        if (!Enabled) return;
        var store = NewStore();
        var key = Key("640.webp");
        var other = key.Replace("640.webp", "960.webp");
        try
        {
            var url = await store.PresignPutAsync(Priv, key, "image/webp", TimeSpan.FromMinutes(5));
            var tampered = url.Replace(key, other); // keys are URL-safe, so they appear verbatim in the path
            Assert.NotEqual(url, tampered);

            Assert.Equal(HttpStatusCode.Forbidden, await PutAsync(tampered, [1, 2, 3], "image/webp"));
            Assert.False((await store.HeadAsync(Priv, other)).exists);
            Assert.False((await store.HeadAsync(Priv, key)).exists);
        }
        finally { await Cleanup(store, Priv, key, other); }
    }

    protected async Task Put_with_other_content_type_is_403_and_object_unchanged()
    {
        if (!Enabled) return;
        var store = NewStore();
        var key = Key("640.avif");
        try
        {
            var url = await store.PresignPutAsync(Priv, key, "image/avif", TimeSpan.FromMinutes(5));

            // Refused before any upload: the key stays absent.
            Assert.Equal(HttpStatusCode.Forbidden, await PutAsync(url, [9, 9, 9, 9], "image/webp"));
            Assert.False((await store.HeadAsync(Priv, key)).exists);

            // Refused after a valid upload: the stored object is unchanged.
            Assert.Equal(HttpStatusCode.OK, await PutAsync(url, [1, 2, 3], "image/avif"));
            var before = await store.HeadAsync(Priv, key);
            Assert.Equal(HttpStatusCode.Forbidden, await PutAsync(url, [9, 9, 9, 9], "image/jpeg"));
            var after = await store.HeadAsync(Priv, key);
            Assert.True(after.exists);
            Assert.Equal(before.etag, after.etag);
            Assert.Equal(3, after.length);
        }
        finally { await Cleanup(store, Priv, key); }
    }

    protected async Task Put_after_expiry_is_403_and_object_absent()
    {
        if (!Enabled) return;
        var store = NewStore();
        var key = Key("master.jpg");
        try
        {
            var url = await store.PresignPutAsync(Priv, key, "image/jpeg", TimeSpan.FromSeconds(1));
            await Task.Delay(TimeSpan.FromSeconds(3));
            Assert.Equal(HttpStatusCode.Forbidden, await PutAsync(url, [1, 2, 3], "image/jpeg"));
            Assert.False((await store.HeadAsync(Priv, key)).exists);
        }
        finally { await Cleanup(store, Priv, key); }
    }

    // ── T017: copy-if-match, get/head ETag, list, delete-prefix ─────────────────────────────────

    protected async Task CopyIfMatch_with_current_etag_copies_with_immutable_cache_control()
    {
        if (!Enabled) return;
        var store = NewStore();
        var src = Key("320.webp");
        var dst = Key("pub.webp");
        try
        {
            await store.PutAsync(Priv, src, Bytes("validated"), "image/webp", "no-cache");
            var etag = (await store.HeadAsync(Priv, src)).etag!;

            Assert.Equal(CopyOutcome.Copied,
                await store.CopyIfMatchAsync(Priv, src, Pub, dst, etag, "image/webp", Immutable));

            var meta = await new S3ClientFactory(Options).CreateClient().GetObjectMetadataAsync(Pub, dst);
            Assert.Equal(Immutable, meta.Headers.CacheControl);
            Assert.Equal("image/webp", meta.Headers.ContentType);
            Assert.Equal(9, meta.ContentLength);
        }
        finally
        {
            await Cleanup(store, Priv, src);
            await Cleanup(store, Pub, dst);
        }
    }

    // [SAFETY] plan item 6, probe P3, decisions row 27: a re-PUT after validation is never published.
    protected async Task CopyIfMatch_after_reput_is_precondition_failed_and_destination_absent()
    {
        if (!Enabled) return;
        var store = NewStore();
        var src = Key("320.avif");
        var dst = Key("pub.avif");
        try
        {
            await store.PutAsync(Priv, src, Bytes("validated"), "image/avif", "no-cache");
            var validatedEtag = (await store.HeadAsync(Priv, src)).etag!;

            await store.PutAsync(Priv, src, Bytes("swapped-after-validation"), "image/avif", "no-cache");

            Assert.Equal(CopyOutcome.PreconditionFailed,
                await store.CopyIfMatchAsync(Priv, src, Pub, dst, validatedEtag, "image/avif", Immutable));
            Assert.False((await store.HeadAsync(Pub, dst)).exists);
        }
        finally
        {
            await Cleanup(store, Priv, src);
            await Cleanup(store, Pub, dst);
        }
    }

    protected async Task Get_etag_equals_head_etag()
    {
        if (!Enabled) return;
        var store = NewStore();
        var key = Key("640.webp");
        try
        {
            await store.PutAsync(Priv, key, Bytes("0123456789"), "image/webp", "no-cache");
            var head = await store.HeadAsync(Priv, key);
            var (bytes, etag) = await store.GetAsync(Priv, key, 8 * 1024 * 1024);

            Assert.True(head.exists);
            Assert.False(string.IsNullOrEmpty(etag));
            Assert.Equal(head.etag, etag);
            Assert.Equal("0123456789", Encoding.ASCII.GetString(bytes));
            Assert.Equal(10, head.length);
        }
        finally { await Cleanup(store, Priv, key); }
    }

    protected async Task Get_over_max_bytes_throws()
    {
        if (!Enabled) return;
        var store = NewStore();
        var key = Key("big.webp");
        try
        {
            await store.PutAsync(Priv, key, Bytes("0123456789"), "image/webp", "no-cache");
            var ex = await Assert.ThrowsAsync<ObjectTooLargeException>(() => store.GetAsync(Priv, key, 9));
            Assert.Equal(10, ex.Length);
            Assert.Equal(9, ex.MaxBytes);
            Assert.Equal(10, (await store.GetAsync(Priv, key, 10)).bytes.Length);
        }
        finally { await Cleanup(store, Priv, key); }
    }

    protected async Task Head_on_missing_key_is_absent()
    {
        if (!Enabled) return;
        var head = await NewStore().HeadAsync(Priv, Key("missing"));
        Assert.False(head.exists);
        Assert.Equal(0, head.length);
        Assert.Null(head.etag);
    }

    protected async Task List_returns_exactly_the_keys_under_a_prefix()
    {
        if (!Enabled) return;
        var store = NewStore();
        var prefix = $"{Prefix}{Guid.NewGuid():N}/";
        string[] inside = [$"{prefix}a/master.jpg", $"{prefix}a/320.avif", $"{prefix}a/320.webp", $"{prefix}b/master.jpg", $"{prefix}c.bin"];
        var outside = $"{prefix.TrimEnd('/')}-sibling/x.bin";
        try
        {
            foreach (var k in inside.Append(outside))
                await store.PutAsync(Priv, k, Bytes("x"), "application/octet-stream", "no-cache");

            // pageSize 2 forces three pages, so continuation is exercised.
            var listed = await store.ListAsync(Priv, prefix, pageSize: 2);
            Assert.Equal(inside.Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));

            var sub = await store.ListAsync(Priv, $"{prefix}a/");
            Assert.Equal(inside.Take(3).Order(StringComparer.Ordinal), sub.Order(StringComparer.Ordinal));
        }
        finally { await Cleanup(store, Priv, [.. inside, outside]); }
    }

    protected async Task DeletePrefix_leaves_zero_keys()
    {
        if (!Enabled) return;
        var store = NewStore();
        var prefix = $"{Prefix}{Guid.NewGuid():N}/";
        string[] keys = [$"{prefix}a/master.jpg", $"{prefix}a/320.avif", $"{prefix}b/master.jpg"];
        var outside = $"{prefix.TrimEnd('/')}-sibling/x.bin";
        var log = new UploadListLogger();
        try
        {
            foreach (var k in keys.Append(outside))
                await store.PutAsync(Priv, k, Bytes("x"), "application/octet-stream", "no-cache");

            await store.DeletePrefixAsync(Priv, prefix, log);

            Assert.Empty(await store.ListAsync(Priv, prefix));
            Assert.True((await store.HeadAsync(Priv, outside)).exists);
            Assert.Empty(log.Lines);
        }
        finally { await Cleanup(store, Priv, [.. keys, outside]); }
    }

    protected async Task DeletePrefix_on_empty_prefix_is_a_no_op()
    {
        if (!Enabled) return;
        var log = new UploadListLogger();
        await NewStore().DeletePrefixAsync(Priv, $"{Prefix}{Guid.NewGuid():N}/", log);
        Assert.Empty(log.Lines);
    }
}

[Collection("s3")]
public sealed class EmulatorUploadStorageTests(S3ProxyFixture fx) : UploadStorageTests
{
    protected override StorageOptions Options => fx.StorageOptions;
    protected override bool Enabled => true;
    protected override string Prefix => "test/";

    [Fact] public Task Presign_signs_content_type_and_host_only_Emulator() => Presign_signs_content_type_and_host_only();
    [Fact] public Task Get_on_put_url_403_Emulator() => Get_on_put_url_is_403_and_object_unchanged();
    [Fact] public Task Put_other_key_403_Emulator() => Put_to_other_key_is_403_and_other_key_absent();
    [Fact] public Task Put_other_content_type_403_Emulator() => Put_with_other_content_type_is_403_and_object_unchanged();
    [Fact] public Task Put_after_expiry_403_Emulator() => Put_after_expiry_is_403_and_object_absent();
    [Fact] public Task CopyIfMatch_copies_Emulator() => CopyIfMatch_with_current_etag_copies_with_immutable_cache_control();
    [Fact] public Task CopyIfMatch_after_reput_refused_Emulator() => CopyIfMatch_after_reput_is_precondition_failed_and_destination_absent();
    [Fact] public Task Get_etag_equals_head_Emulator() => Get_etag_equals_head_etag();
    [Fact] public Task Get_over_max_throws_Emulator() => Get_over_max_bytes_throws();
    [Fact] public Task Head_missing_Emulator() => Head_on_missing_key_is_absent();
    [Fact] public Task List_exact_Emulator() => List_returns_exactly_the_keys_under_a_prefix();
    [Fact] public Task DeletePrefix_empty_Emulator() => DeletePrefix_leaves_zero_keys();
    [Fact] public Task DeletePrefix_no_op_Emulator() => DeletePrefix_on_empty_prefix_is_a_no_op();
}

[Trait("Category", "R2")]
public sealed class R2UploadStorageTests : UploadStorageTests, IClassFixture<R2Fixture>
{
    private readonly R2Fixture _fx;
    private readonly string _prefix = $"test/{Guid.NewGuid():N}/";

    public R2UploadStorageTests(R2Fixture fx) => _fx = fx;

    protected override StorageOptions Options => _fx.StorageOptions;
    protected override bool Enabled => _fx.Enabled;
    protected override string Prefix => _prefix;

    [Fact] public Task Presign_signs_content_type_and_host_only_R2() => Presign_signs_content_type_and_host_only();
    [Fact] public Task Get_on_put_url_403_R2() => Get_on_put_url_is_403_and_object_unchanged();
    [Fact] public Task Put_other_key_403_R2() => Put_to_other_key_is_403_and_other_key_absent();
    [Fact] public Task Put_other_content_type_403_R2() => Put_with_other_content_type_is_403_and_object_unchanged();
    [Fact] public Task Put_after_expiry_403_R2() => Put_after_expiry_is_403_and_object_absent();
    [Fact] public Task CopyIfMatch_copies_R2() => CopyIfMatch_with_current_etag_copies_with_immutable_cache_control();
    [Fact] public Task CopyIfMatch_after_reput_refused_R2() => CopyIfMatch_after_reput_is_precondition_failed_and_destination_absent();
    [Fact] public Task Get_etag_equals_head_R2() => Get_etag_equals_head_etag();
    [Fact] public Task Get_over_max_throws_R2() => Get_over_max_bytes_throws();
    [Fact] public Task Head_missing_R2() => Head_on_missing_key_is_absent();
    [Fact] public Task List_exact_R2() => List_returns_exactly_the_keys_under_a_prefix();
    [Fact] public Task DeletePrefix_empty_R2() => DeletePrefix_leaves_zero_keys();
    [Fact] public Task DeletePrefix_no_op_R2() => DeletePrefix_on_empty_prefix_is_a_no_op();
}

/// <summary>TryDeleteAsync logs and never rethrows (FR-036, decisions row 44). No storage needed.</summary>
public sealed class UploadStorageTryDeleteTests
{
    [Fact]
    public async Task TryDelete_logs_cleanup_failed_with_type_name_and_does_not_throw()
    {
        var log = new UploadListLogger();
        await ImageStore.TryDeleteAsync(
            () => throw new InvalidOperationException("secret-detail https://example/presigned?X-Amz-Signature=abc"),
            "staging/b1/i1/a1/320.avif", log);

        var line = Assert.Single(log.Lines);
        Assert.Equal("upload.cleanup_failed staging/b1/i1/a1/320.avif InvalidOperationException", line);
        Assert.Equal(LogLevel.Warning, Assert.Single(log.Levels));
    }

    [Fact]
    public async Task TryDelete_success_logs_nothing()
    {
        var log = new UploadListLogger();
        var ran = 0;
        await ImageStore.TryDeleteAsync(() => { ran++; return Task.CompletedTask; }, "k", log);
        Assert.Equal(1, ran);
        Assert.Empty(log.Lines);
    }
}

internal sealed class UploadListLogger : ILogger
{
    public List<string> Lines { get; } = [];
    public List<LogLevel> Levels { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (Lines)
        {
            Lines.Add(formatter(state, exception));
            Levels.Add(logLevel);
        }
    }
}

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Amazon.S3.Model;
using Gallery.Web.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gallery.Web.Tests.Fixtures;

/// <summary>
/// 008 T013: an <see cref="AdminHost"/> wired for the upload pipeline. Storage points at the
/// S3Proxy emulator (buckets <c>pub</c>/<c>priv</c>), <see cref="TimeProvider"/> is replaced by a
/// <see cref="MutableTimeProvider"/> exposed as <see cref="Clock"/>, the owner <c>owner-1</c> is
/// signed in, and the CSRF token from <c>/admin</c> is cached for JSON posts.
/// </summary>
public sealed class UploadHost : IAsyncDisposable
{
    public const string Owner = "owner-1";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly S3ProxyFixture _s3;

    public AdminHost Host { get; }
    public MutableTimeProvider Clock { get; }
    public string CsrfToken { get; }

    public List<string> LogLines => Host.LogLines;
    public IServiceProvider Services => Host.Services;
    public string DatabaseName => Host.DatabaseName;

    private UploadHost(AdminHost host, MutableTimeProvider clock, string csrf, S3ProxyFixture s3)
    {
        Host = host;
        Clock = clock;
        CsrfToken = csrf;
        _s3 = s3;
    }

    public static async Task<UploadHost> CreateAsync(
        MongoFixture mongo,
        FakeGoogle fakeGoogle,
        S3ProxyFixture s3,
        Dictionary<string, string?>? extraOverrides = null,
        Action<Microsoft.AspNetCore.Builder.WebApplicationBuilder>? configure = null)
    {
        var so = s3.StorageOptions;
        var overrides = new Dictionary<string, string?>
        {
            ["Storage:ServiceUrl"] = so.ServiceUrl,
            ["Storage:AccessKeyId"] = so.AccessKeyId,
            ["Storage:SecretAccessKey"] = so.SecretAccessKey,
            ["Storage:PublicBucket"] = so.PublicBucket,
            ["Storage:PrivateBucket"] = so.PrivateBucket,
        };
        if (extraOverrides is not null)
            foreach (var (k, v) in extraOverrides)
                overrides[k] = v;

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var host = await AdminHost.CreateAsync(mongo, fakeGoogle, overrides, b =>
        {
            b.Services.RemoveAll<TimeProvider>();
            b.Services.AddSingleton<TimeProvider>(clock);
            configure?.Invoke(b);
        });

        try
        {
            var signIn = await host.SignInAsync(Owner);
            if (signIn.StatusCode != System.Net.HttpStatusCode.Found)
                throw new InvalidOperationException($"Sign-in as {Owner} answered {(int)signIn.StatusCode}.");
            var token = await host.TokenAsync("/admin");
            return new UploadHost(host, clock, token, s3);
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    // ── app requests ─────────────────────────────────────────────────────────

    public Task<HttpResponseMessage> GetAsync(string path) => Host.GetAsync(path);

    /// <summary>GETs <paramref name="path"/> and parses the body as JSON (any status).</summary>
    public async Task<(HttpResponseMessage Response, JsonDocument? Body)> GetJsonAsync(string path)
    {
        var resp = await Host.GetAsync(path);
        return (resp, await ParseAsync(resp));
    }

    /// <summary>POSTs <paramref name="body"/> as JSON, with the cached <c>X-CSRF-TOKEN</c> unless
    /// <paramref name="csrf"/> is false.</summary>
    public Task<HttpResponseMessage> PostJsonAsync(string path, object body, bool csrf = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json"),
        };
        if (csrf)
            request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", CsrfToken);
        return SendWithJarAsync(request);
    }

    // AdminHost owns the cookie jar (session + __Host-af antiforgery cookie, which the cached
    // token is bound to); JSON posts go through its SendAsync so the jar is never copied.
    private Task<HttpResponseMessage> SendWithJarAsync(HttpRequestMessage request) =>
        Host.SendAsync(request);

    public static async Task<JsonDocument?> ParseAsync(HttpResponseMessage resp)
    {
        var text = await resp.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text) ? null : JsonDocument.Parse(text);
    }

    // ── storage ──────────────────────────────────────────────────────────────

    /// <summary>
    /// PUTs <paramref name="bytes"/> to a presigned URL as a browser would, with a plain
    /// <see cref="HttpClient"/> sending exactly one header, <c>Content-Type</c>.
    /// </summary>
    public static async Task<HttpResponseMessage> PutToPresignedAsync(string url, byte[] bytes, string contentType)
    {
        using var client = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
        return await client.SendAsync(request);
    }

    /// <summary>Every key under <paramref name="prefix"/> in <paramref name="bucket"/>, through the S3 SDK.</summary>
    public async Task<IReadOnlyList<string>> ListAsync(string bucket, string prefix)
    {
        using var client = new S3ClientFactory(_s3.StorageOptions).CreateClient();
        var keys = new List<string>();
        var request = new ListObjectsV2Request { BucketName = bucket, Prefix = prefix };
        ListObjectsV2Response response;
        do
        {
            response = await client.ListObjectsV2Async(request);
            keys.AddRange((response.S3Objects ?? []).Select(o => o.Key));
            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated == true);
        return keys;
    }

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}

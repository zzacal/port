using System.Text;
using Amazon.S3.Model;
using Gallery.Web.Data;
using Gallery.Web.Edge;
using Gallery.Web.Storage;
using Gallery.Web.Upload;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;

namespace Gallery.Web.Tests.Fixtures;

/// <summary>
/// 009 T020 (research R-12): an <see cref="AdminHost"/> wired for library and gallery management.
/// Storage points at the S3Proxy emulator (buckets <c>pub</c>/<c>priv</c>), <c>Site:OutputCache</c>
/// is off unless the caller overrides it, <see cref="IEdgeProvider"/> is one
/// <see cref="RecordingEdgeProvider"/> (<see cref="Recorder"/>) with <c>Edge:PurgePages=true</c> so
/// page addresses reach the recorder, and the owner <c>owner-1</c> is signed in: the site is claimed
/// through the app's own <c>OwnerStore.TryClaimAsync</c>, the session is minted with
/// <see cref="AdminHost.MintSession(string, DateTimeOffset, DateTimeOffset)"/>, and the CSRF token
/// from <c>/admin</c> is cached. Every request goes through <see cref="AdminHost.SendAsync"/>, so
/// there is one cookie jar.
/// </summary>
public sealed class LibraryHost : IAsyncDisposable
{
    public const string Owner = "owner-1";
    public const string SiteBaseUrl = "https://site.test";
    public const string ImageBaseUrl = "https://img.test";
    public const string Pub = "pub";
    public const string Priv = "priv";

    private readonly S3ProxyFixture _s3;

    public AdminHost Host { get; }
    public RecordingEdgeProvider Recorder { get; }
    public string CsrfToken { get; }

    public List<string> LogLines => Host.LogLines;
    public IServiceProvider Services => Host.Services;

    /// <summary>The host's database (a fresh <c>gallery_test_*</c> per host).</summary>
    public IMongoDatabase Db { get; }

    private LibraryHost(AdminHost host, RecordingEdgeProvider recorder, string csrf, S3ProxyFixture s3, IMongoDatabase db)
    {
        Host = host;
        Recorder = recorder;
        CsrfToken = csrf;
        _s3 = s3;
        Db = db;
    }

    public static async Task<LibraryHost> CreateAsync(
        MongoFixture mongo,
        S3ProxyFixture s3,
        FakeGoogle? fakeGoogle = null,
        Dictionary<string, string?>? extraOverrides = null,
        Action<WebApplicationBuilder>? configure = null)
    {
        var so = s3.StorageOptions;
        var overrides = new Dictionary<string, string?>
        {
            ["Storage:ServiceUrl"] = so.ServiceUrl,
            ["Storage:AccessKeyId"] = so.AccessKeyId,
            ["Storage:SecretAccessKey"] = so.SecretAccessKey,
            ["Storage:PublicBucket"] = so.PublicBucket,
            ["Storage:PrivateBucket"] = so.PrivateBucket,
            ["Storage:ImageBaseUrl"] = ImageBaseUrl,
            ["Site:OutputCache"] = "false",
            ["Edge:SiteBaseUrl"] = SiteBaseUrl,
            ["Edge:PurgePages"] = "true",
        };
        if (extraOverrides is not null)
            foreach (var (k, v) in extraOverrides)
                overrides[k] = v;

        var recorder = new RecordingEdgeProvider();
        var host = await AdminHost.CreateAsync(mongo, fakeGoogle, overrides, b =>
        {
            b.Services.RemoveAll<IEdgeProvider>();
            b.Services.AddSingleton<IEdgeProvider>(recorder);
            configure?.Invoke(b);
        });

        try
        {
            IMongoDatabase db;
            using (var scope = host.Services.CreateScope())
            {
                db = scope.ServiceProvider.GetRequiredService<IMongoDatabase>();
                var claim = await scope.ServiceProvider.GetRequiredService<OwnerStore>()
                    .TryClaimAsync(Owner, "127.0.0.1", DateTime.UtcNow);
                if (claim != ClaimResult.Won)
                    throw new InvalidOperationException($"Claiming the site as {Owner} answered {claim}.");
            }

            var now = DateTimeOffset.UtcNow;
            host.SetSessionCookie(host.MintSession(Owner, now, now.AddDays(7)));
            var token = await host.TokenAsync("/admin");
            return new LibraryHost(host, recorder, token, s3, db);
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    // ── services ─────────────────────────────────────────────────────────────

    /// <summary>A DI scope (one per simulated request); resolve services from it.</summary>
    public IServiceScope Scope() => Host.Services.CreateScope();

    /// <summary>Runs <paramref name="call"/> on a <typeparamref name="TService"/> from a fresh scope (one request).</summary>
    public async Task<T> RunAsync<TService, T>(Func<TService, Task<T>> call) where TService : notnull
    {
        using var scope = Scope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }

    /// <summary>
    /// After an operation: waits for the edge queue to drain, checks every data-model invariant
    /// (<see cref="InvariantChecker"/>), and returns the recorder's purged addresses (sorted), then
    /// clears the recorder for the next operation.
    /// </summary>
    public async Task<IReadOnlyList<string>> SettleAsync()
    {
        await EdgeIdleAsync();
        await InvariantChecker.AssertAsync(Db);
        var purged = Recorder.AllPurged();
        Recorder.Clear();
        return purged;
    }

    /// <summary>
    /// Waits until the edge queue is empty and no work item is in flight (the recorder then holds
    /// every call of the operations so far). Fails the test after 10 s.
    /// </summary>
    public async Task EdgeIdleAsync()
    {
        var idle = await Host.Services.GetRequiredService<EdgeQueue>().WhenIdleAsync(TimeSpan.FromSeconds(10));
        if (!idle) throw new TimeoutException("The edge queue did not drain within 10 s.");
    }

    /// <summary>An absolute page address on <see cref="SiteBaseUrl"/>.</summary>
    public static string Page(string path) => SiteBaseUrl + path;

    /// <summary>An absolute file address on <see cref="ImageBaseUrl"/>.</summary>
    public static string File(string key) => ImageBaseUrl + "/" + key;

    // ── app requests ─────────────────────────────────────────────────────────

    public Task<HttpResponseMessage> GetAsync(string path) => Host.GetAsync(path);

    /// <summary>
    /// POSTs <paramref name="fields"/> as a form (repeated keys allowed), adding the cached
    /// <c>__RequestVerificationToken</c> unless <paramref name="csrf"/> is false.
    /// </summary>
    public Task<HttpResponseMessage> PostFormAsync(
        string path, IEnumerable<KeyValuePair<string, string>> fields, bool csrf = true)
    {
        var all = fields.ToList();
        if (csrf) all.Add(new("__RequestVerificationToken", CsrfToken));
        return Host.SendAsync(new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(all) });
    }

    /// <summary>
    /// A POST with no body and no content type. With <paramref name="csrfHeader"/> the cached token
    /// is sent as the <c>X-CSRF-TOKEN</c> header only.
    /// </summary>
    public Task<HttpResponseMessage> PostBareAsync(string path, bool csrfHeader = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (csrfHeader)
            request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", CsrfToken);
        return Host.SendAsync(request);
    }

    // ── data ─────────────────────────────────────────────────────────────────

    public IMongoCollection<ImageDoc> Images => Db.GetCollection<ImageDoc>("images");
    public IMongoCollection<GalleryDoc> Galleries => Db.GetCollection<GalleryDoc>("galleries");
    public IMongoCollection<SiteDoc> Site => Db.GetCollection<SiteDoc>("site");

    /// <summary>
    /// The site document's 009 fields (<c>homeOrder</c>, <c>homeCards</c>, <c>updatedAt</c>). The
    /// host's site is claimed, and <see cref="SiteDoc"/> does not map the claim fields, so the whole
    /// document cannot be read as one.
    /// </summary>
    public Task<SiteDoc> SiteAsync() =>
        Site.Find(s => s.Id == "site")
            .Project<SiteDoc>(Builders<SiteDoc>.Projection.Include(s => s.HomeOrder).Include(s => s.HomeCards).Include(s => s.UpdatedAt))
            .FirstAsync();

    /// <summary>
    /// Inserts an upload-shaped image (<see cref="SeedShapes.UploadedImage"/>: no <c>seedKey</c>)
    /// and, unless <paramref name="files"/> is false, PUTs its derivative files to <c>pub</c> and
    /// <c>masters/{id}.jpg</c> to <c>priv</c>. <paramref name="shape"/> may adjust the document
    /// before it is stored (e.g. to share a derivative key with another image).
    /// </summary>
    public async Task<ImageDoc> InsertUploadedImageAsync(
        DateTime? createdAt = null, string? title = null, bool files = true, Action<ImageDoc>? shape = null)
    {
        var image = SeedShapes.UploadedImage(createdAt: createdAt, title: title);
        shape?.Invoke(image);
        if (files) await PutFilesAsync(image, master: true);
        await Images.InsertOneAsync(image);
        return image;
    }

    /// <summary>
    /// Inserts a seed-shaped image (<see cref="SeedShapes.SeededImage"/>: non-empty <c>seedKey</c>)
    /// and PUTs its derivative files to <c>pub</c>. Seeded images have no master in <c>priv</c>
    /// unless <paramref name="master"/> is true.
    /// </summary>
    public async Task<ImageDoc> InsertSeededAsync(DateTime? createdAt = null, bool master = false)
    {
        var image = SeedShapes.SeededImage(createdAt: createdAt);
        await PutFilesAsync(image, master);
        await Images.InsertOneAsync(image);
        return image;
    }

    /// <summary>PUTs every derivative key of <paramref name="image"/> to <c>pub</c> (and its master to <c>priv</c>).</summary>
    public async Task PutFilesAsync(ImageDoc image, bool master)
    {
        using var client = new S3ClientFactory(_s3.StorageOptions).CreateClient();
        var puts = image.Derivatives
            .SelectMany(d => new[] { d.Avif, d.Webp })
            .Select(k => (Pub, k))
            .ToList();
        if (master) puts.Add((Priv, ConfirmService.MasterKey(image.Id)));
        foreach (var (bucket, key) in puts)
            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = new MemoryStream(Encoding.ASCII.GetBytes(key)),
                DisableDefaultChecksumValidation = true,
            });
    }

    /// <summary>Every key in <paramref name="bucket"/> (optionally under <paramref name="prefix"/>), through the S3 SDK.</summary>
    public async Task<IReadOnlyList<string>> ListAsync(string bucket, string prefix = "")
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

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Gallery.Web.Storage;
using Gallery.Web.Tests.Fixtures;
using Xunit.Abstractions;

namespace Gallery.Web.Tests;

/// <summary>
/// [SAFETY] FR-021 / III.9: the dev R2 credential is scoped to the two dev buckets. Anything
/// outside them must be refused. Runs only with GALLERY_R2_TESTS=1 (needs the owner's secrets).
/// </summary>
[Trait("Category", "R2")]
public sealed class R2ScopeTests(R2Fixture fx, ITestOutputHelper output) : IClassFixture<R2Fixture>
{
    private AmazonS3Client Client() => new S3ClientFactory(fx.StorageOptions).CreateClient();

    [Fact]
    public async Task List_buckets_is_forbidden_R2()
    {
        if (!fx.Enabled) return;
        var ex = await Assert.ThrowsAsync<AmazonS3Exception>(() => Client().ListBucketsAsync());
        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    [Fact]
    public async Task Other_bucket_is_unreachable_R2()
    {
        if (!fx.Enabled) return;
        var ex = await Assert.ThrowsAsync<AmazonS3Exception>(() =>
            Client().ListObjectsV2Async(new ListObjectsV2Request { BucketName = "gallery-scope-probe-other" }));
        output.WriteLine($"other bucket list -> {(int)ex.StatusCode}");
        Assert.Contains(ex.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });
    }

    [Fact]
    public async Task Create_bucket_is_forbidden_R2()
    {
        if (!fx.Enabled) return;
        var ex = await Assert.ThrowsAsync<AmazonS3Exception>(() =>
            Client().PutBucketAsync("gallery-scope-probe-new"));
        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    [Theory]
    [InlineData("gallery-dev-public")]
    [InlineData("gallery-dev-private")]
    public async Task Dev_buckets_are_listable_R2(string bucket)
    {
        if (!fx.Enabled) return;
        var r = await Client().ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket, MaxKeys = 1 });
        Assert.Equal(HttpStatusCode.OK, r.HttpStatusCode);
    }
}

using Gallery.Web.Tests.Fixtures;

namespace Gallery.Web.Tests;

/// <summary>
/// 020 T004 capture evidence (SC-011, "every public page byte-for-byte as before"). Builds
/// <see cref="HomeCaptureLibrary"/> and writes every public page type through
/// <see cref="PublicCapture"/> to the directory in env <c>PUBLIC_CAPTURE_DIR</c> (default
/// specs/020-admin-workspace/evidence/pre-public). evidence/compare-public.sh runs it into /tmp after
/// the change and diffs the two directories.
/// </summary>
[Collection("s3")]
[Trait("Category", "Evidence")]
public class PublicCaptureEvidenceTests(S3ProxyFixture s3, MongoFixture mongo) : IClassFixture<MongoFixture>
{
    [Fact]
    public async Task Every_public_page_type_is_written_for_the_comparison()
    {
        await using var h = await LibraryHost.CreateAsync(mongo, s3);
        var built = await HomeCaptureLibrary.BuildAsync(h);

        var pages = await PublicCapture.CaptureAsync(h, built);

        var dir = Environment.GetEnvironmentVariable("PUBLIC_CAPTURE_DIR") is { Length: > 0 } d
            ? d
            : Path.Combine(Render.RepoRoot, "specs", "020-admin-workspace", "evidence", "pre-public");
        PublicCapture.Write(dir, pages);

        // Three listed galleries, each with a gallery page and a photo page, plus home, listing,
        // cameras and the unknown-gallery page.
        Assert.True(pages.Count >= 10, $"{pages.Count} pages captured");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, pages.Single(p => p.Label == "unknown-gallery").Status);
        Assert.All(pages.Where(p => p.Label != "unknown-gallery"), p => Assert.Equal(System.Net.HttpStatusCode.OK, p.Status));
    }
}

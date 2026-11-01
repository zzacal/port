using Gallery.Web.Library;
using Gallery.Web.Storage;
using Gallery.Web.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gallery.Web.Tests;

/// <summary>
/// 009 T027 (research R-05, probe P3): the storage behaviour image delete relies on, against the
/// S3Proxy emulator (every run) and real dev R2 (on demand, <c>GALLERY_R2_TESTS=1</c>), the same
/// abstract/emulator/R2 pattern as <see cref="StorageTests"/>. Deleting a key that does not exist is
/// not an error, so a missing master or derivative counts as deleted.
/// </summary>
public abstract class LibraryStorageTests
{
    protected abstract StorageOptions Options { get; }
    protected abstract bool Enabled { get; }
    protected abstract string Prefix { get; }

    private IObjectDeleter NewDeleter() =>
        new ImageStoreObjectDeleter(new ImageStore(new S3ClientFactory(Options), NullLogger<ImageStore>.Instance));

    protected async Task Deleting_a_missing_key_does_not_throw()
    {
        if (!Enabled) return;
        var deleter = NewDeleter();
        var missingPublic = $"{Prefix}{Guid.NewGuid():N}.avif";
        var missingMaster = $"{Prefix}masters/{Guid.NewGuid():N}.jpg";

        var ex1 = await Record.ExceptionAsync(() => deleter.DeleteAsync(Options.PublicBucket!, missingPublic));
        var ex2 = await Record.ExceptionAsync(() => deleter.DeleteAsync(Options.PrivateBucket!, missingMaster));

        Assert.Null(ex1);
        Assert.Null(ex2);
        Assert.False(await new ImageStore(new S3ClientFactory(Options), NullLogger<ImageStore>.Instance)
            .ExistsAsync(Options.PublicBucket!, missingPublic));
    }
}

[Collection("s3")]
public sealed class EmulatorLibraryStorageTests(S3ProxyFixture fx) : LibraryStorageTests
{
    protected override StorageOptions Options => fx.StorageOptions;
    protected override bool Enabled => true;
    protected override string Prefix => "test/";

    [Fact] public Task Delete_missing_key() => Deleting_a_missing_key_does_not_throw();
}

[Trait("Category", "R2")]
public sealed class R2LibraryStorageTests : LibraryStorageTests, IClassFixture<R2Fixture>
{
    private readonly R2Fixture _fx;
    private readonly string _prefix = $"test/{Guid.NewGuid():N}/";

    public R2LibraryStorageTests(R2Fixture fx) => _fx = fx;

    protected override StorageOptions Options => _fx.StorageOptions;
    protected override bool Enabled => _fx.Enabled;
    protected override string Prefix => _prefix;

    [Fact] public Task Delete_missing_key_R2() => Deleting_a_missing_key_does_not_throw();
}

using Gallery.Web.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Gallery.Web.Tests;

/// <summary>
/// T011 (012 contracts/hosting.md "Tests", R-05): with Hosting:DataProtectionKeysPath set, keys persist
/// to disk so a payload protected before a restart still unprotects after it.
/// </summary>
public class HostingDataProtectionTests
{
    private const string Purpose = "gallery.hosting.test";
    private const string Payload = "owner-session-survives-restart";

    private static WebApplication BuildHost(string keysPath) =>
        Gallery.Web.AppBuilder.Build(
            ["--urls", "http://127.0.0.1:0"],
            new Dictionary<string, string?>
            {
                ["Mongo:ConnectionString"] = "mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=500",
                ["Mongo:Database"] = "gallery_test_dp",
                ["Storage:ImageBaseUrl"] = "https://img.test",
                ["Storage:ServiceUrl"] = "http://localhost:19999",
                ["Storage:AccessKeyId"] = "local",
                ["Storage:SecretAccessKey"] = "localsecret",
                ["Storage:PublicBucket"] = "pub",
                ["Storage:PrivateBucket"] = "priv",
                ["Site:OutputCache"] = "false",
                ["Hosting:DataProtectionKeysPath"] = keysPath,
            });

    [Fact]
    public async Task Payload_protected_by_host1_unprotects_in_host2_on_same_key_path()
    {
        var keys = Path.Combine(Path.GetTempPath(), "gallery-dp-" + Guid.NewGuid().ToString("N"));
        try
        {
            string protectedPayload;
            var host1 = BuildHost(keys);
            await host1.StartAsync();
            try
            {
                protectedPayload = host1.Services.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector(Purpose).Protect(Payload);
            }
            finally
            {
                await host1.StopAsync();
                await host1.DisposeAsync();
            }

            Assert.Single(Directory.GetFiles(keys, "key-*.xml"));

            var host2 = BuildHost(keys);
            await host2.StartAsync();
            try
            {
                var unprotected = host2.Services.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector(Purpose).Unprotect(protectedPayload);
                Assert.Equal(Payload, unprotected);
            }
            finally
            {
                await host2.StopAsync();
                await host2.DisposeAsync();
            }

            // Host 2 reused host 1's key rather than minting a new one.
            Assert.Single(Directory.GetFiles(keys, "key-*.xml"));
        }
        finally
        {
            if (Directory.Exists(keys)) Directory.Delete(keys, recursive: true);
        }
    }

    [Fact]
    public void Missing_key_directory_is_created()
    {
        var keys = Path.Combine(Path.GetTempPath(), "gallery-dp-" + Guid.NewGuid().ToString("N"), "nested");
        try
        {
            Assert.False(Directory.Exists(keys));
            var app = BuildHost(keys);
            try
            {
                Assert.True(Directory.Exists(keys));
            }
            finally
            {
                ((IDisposable)app).Dispose();
            }
        }
        finally
        {
            var parent = Path.GetDirectoryName(keys)!;
            if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }
}

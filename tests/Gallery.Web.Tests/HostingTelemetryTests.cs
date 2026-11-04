using System.Net;
using Gallery.Web.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace Gallery.Web.Tests;

/// <summary>
/// T012 (012 contracts/hosting.md "AddHosting" step 5, probe-otel-wiring as a test): Azure Monitor
/// export is wired only when APPLICATIONINSIGHTS_CONNECTION_STRING is set, and an unreachable
/// ingestion endpoint never blocks startup or requests.
/// </summary>
[Collection("mongo")]
public class HostingTelemetryTests(MongoFixture mongo)
{
    private const string UnreachableCs =
        "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://127.0.0.1:9/";

    private WebApplication BuildHost(string? connectionString)
    {
        var db = mongo.NewDatabase();
        var overrides = new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = mongo.ConnectionString,
            ["Mongo:Database"] = db.DatabaseNamespace.DatabaseName,
            ["Storage:ImageBaseUrl"] = "https://img.test",
            ["Storage:ServiceUrl"] = "http://localhost:19999",
            ["Storage:AccessKeyId"] = "local",
            ["Storage:SecretAccessKey"] = "localsecret",
            ["Storage:PublicBucket"] = "pub",
            ["Storage:PrivateBucket"] = "priv",
            ["Site:OutputCache"] = "false",
            // Explicit empty value so an ambient environment variable cannot turn export on.
            ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = connectionString ?? "",
        };
        return Gallery.Web.AppBuilder.Build(["--urls", "http://127.0.0.1:0"], overrides);
    }

    [Fact]
    public async Task No_connection_string_registers_no_TracerProvider()
    {
        var app = BuildHost(null);
        try
        {
            Assert.Null(app.Services.GetService<TracerProvider>());
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Unreachable_connection_string_host_starts_and_home_answers_200()
    {
        var app = BuildHost(UnreachableCs);
        try
        {
            Assert.NotNull(app.Services.GetService<TracerProvider>());
            await app.StartAsync();

            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(app.Urls.First()),
            };
            var resp = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            await app.StopAsync();
        }
        finally
        {
            await app.DisposeAsync();
        }
    }
}

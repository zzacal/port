using System.Text.RegularExpressions;
using Gallery.Web.Admin.Auth;
using Gallery.Web.Data;
using Gallery.Web.Hosting;
using Gallery.Web.Storage;
using Microsoft.Extensions.Configuration;
using InputAtom = Gallery.Web.Components.Atoms.Input;

namespace Gallery.Web.Tests;

/// <summary>
/// 018 T008 [SAFETY] (data-model.md LocalSignIn, research R-01/R-03/R-04, contracts/components.md K1):
/// the pure local sign-in rule, the constant-time password match, the production refusal of the
/// key, and the Input atom's <c>type</c> parameter. No host.
/// </summary>
public class LocalSignInRuleTests
{
    private const string Pw15 = "abcdefghijklmn5";   // 15 characters
    private const string Pw16 = "abcdefghijklmno6";  // 16 characters

    // Copied from ProductionGuardTests.Valid() (that file is not edited).
    private static Dictionary<string, string?> Valid() => new()
    {
        ["Mongo:ConnectionString"] = "mongodb+srv://gallery-app:***@cluster0.example.mongodb.net/",
        ["Mongo:Database"] = "gallery",
        ["Storage:ServiceUrl"] = "https://acct.r2.cloudflarestorage.com",
        ["Storage:AccessKeyId"] = "prod-access-key-id",
        ["Storage:SecretAccessKey"] = "prod-secret-access-key",
        ["Storage:PublicBucket"] = "gallery-public",
        ["Storage:PrivateBucket"] = "gallery-private",
        ["Storage:ImageBaseUrl"] = "https://img.example.com",
        ["Authentication:Google:ClientId"] = "prod-client.apps.googleusercontent.com",
        ["Authentication:Google:ClientSecret"] = "prod-google-client-secret",
        ["Edge:ZoneId"] = "prod-zone-id",
        ["Edge:ApiToken"] = "prod-api-token",
        ["Edge:PurgePages"] = "true",
        ["Edge:SiteBaseUrl"] = "https://gallery.example.com",
        ["Site:OutputCache"] = "true",
        ["Hosting:Edge"] = "cloudflare",
        ["Hosting:OriginSecret"] = "pg-origin-secret-0123456789abcdefghijklm",
        ["Hosting:DataProtectionKeysPath"] = "/home/data/keys",
        ["APPLICATIONINSIGHTS_CONNECTION_STRING"] =
            "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://127.0.0.1:9/",
    };

    private static IConfiguration Config(IDictionary<string, string?> d) =>
        new ConfigurationBuilder().AddInMemoryCollection(d).Build();

    private static IConfiguration PasswordConfig(string? password) =>
        Config(password is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { [LocalSignIn.PasswordKey] = password });

    private static StorageOptions Storage(string bucket) => new() { PublicBucket = bucket };

    private static MongoOptions Mongo(string database) => new() { Database = database };

    private static LocalSignIn DevInstance(string? password) =>
        new(PasswordConfig(password), "Development", Storage("gallery-dev-p"), Mongo("x_dev"));

    // ── Evaluate truth table ────────────────────────────────────────────────

    public static TheoryData<string, string, string, string?, LocalSignInState> Table()
    {
        var data = new TheoryData<string, string, string, string?, LocalSignInState>();
        foreach (var env in new[] { "Development", "Production" })
        foreach (var db in new[] { "x_dev", "x" })
        foreach (var bucket in new[] { "gallery-dev-p", "p" })
        foreach (var pw in new string?[] { null, "   ", Pw15, Pw16 })
        {
            var devHost = env == "Development" && db == "x_dev" && bucket == "gallery-dev-p";
            LocalSignInState expected;
            if (string.IsNullOrWhiteSpace(pw)) expected = LocalSignInState.Absent;
            else if (!devHost) expected = LocalSignInState.NotDevelopmentHost;
            else if (pw.Length == 15) expected = LocalSignInState.TooShort;
            else expected = LocalSignInState.Enabled;
            data.Add(env, db, bucket, pw, expected);
        }
        return data;
    }

    [Fact]
    public void Truth_table_covers_all_32_shapes_and_has_exactly_one_Enabled_and_one_TooShort()
    {
        var rows = Table().Select(r => (LocalSignInState)r[4]).ToList();
        Assert.Equal(32, rows.Count);
        Assert.Equal(1, rows.Count(s => s == LocalSignInState.Enabled));
        Assert.Equal(1, rows.Count(s => s == LocalSignInState.TooShort));
        Assert.Equal(16, rows.Count(s => s == LocalSignInState.Absent));
        Assert.Equal(14, rows.Count(s => s == LocalSignInState.NotDevelopmentHost));
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void Evaluate_follows_the_truth_table(
        string env, string db, string bucket, string? password, LocalSignInState expected)
    {
        var state = LocalSignIn.Evaluate(PasswordConfig(password), env, Storage(bucket), Mongo(db));
        Assert.Equal(expected, state);

        var instance = new LocalSignIn(PasswordConfig(password), env, Storage(bucket), Mongo(db));
        Assert.Equal(expected, instance.State);
        Assert.Equal(expected == LocalSignInState.Enabled, instance.Enabled);
    }

    [Fact]
    public void Constants_are_verbatim()
    {
        Assert.Equal("LocalSignIn:Password", LocalSignIn.PasswordKey);
        Assert.Equal(16, LocalSignIn.MinLength);
        Assert.Equal("local-owner", LocalSignIn.LocalOwner);
    }

    // ── Matches ─────────────────────────────────────────────────────────────

    [Fact]
    public void Matches_true_only_for_the_configured_password()
    {
        var local = DevInstance(Pw16);
        Assert.True(local.Enabled);
        Assert.True(local.Matches(Pw16));
        Assert.False(local.Matches("abcdefghijklmnoX"));      // different, same length
        Assert.False(local.Matches(Pw16 + "7"));              // different length
        Assert.False(local.Matches(Pw16[..15]));              // prefix
        Assert.False(local.Matches(""));
        Assert.False(local.Matches(null));
    }

    [Fact]
    public void Disabled_instance_never_matches_even_the_configured_value()
    {
        var tooShort = DevInstance(Pw15);
        Assert.Equal(LocalSignInState.TooShort, tooShort.State);
        Assert.False(tooShort.Matches(Pw15));

        var notDev = new LocalSignIn(PasswordConfig(Pw16), "Production", Storage("gallery-dev-p"), Mongo("x_dev"));
        Assert.Equal(LocalSignInState.NotDevelopmentHost, notDev.State);
        Assert.False(notDev.Matches(Pw16));

        var absent = DevInstance(null);
        Assert.Equal(LocalSignInState.Absent, absent.State);
        Assert.False(absent.Matches(""));
        Assert.False(absent.Matches(Pw16));
    }

    // ── ProductionGuard rule (R-04) ─────────────────────────────────────────

    [Fact]
    public void ProductionGuard_refuses_the_key_naming_it_never_the_value()
    {
        const string value = "prod-local-pw-0123456789";
        var d = Valid();
        d[LocalSignIn.PasswordKey] = value;

        var failures = ProductionGuard.Check(Config(d));

        Assert.Equal(["LocalSignIn:Password must not be set"], failures);
        Assert.DoesNotContain(value, failures[0], StringComparison.Ordinal);
        Assert.DoesNotContain("prod-local", failures[0], StringComparison.Ordinal);
        Assert.DoesNotContain("0123456789", failures[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionGuard_refuses_the_key_even_when_empty()
    {
        var d = Valid();
        d[LocalSignIn.PasswordKey] = "";
        Assert.Equal(["LocalSignIn:Password must not be set"], ProductionGuard.Check(Config(d)));
    }

    // ── K1 Input.Type ───────────────────────────────────────────────────────

    private static string InputTag(string html)
    {
        var m = Regex.Match(html, "<input\\b[^>]*>");
        Assert.True(m.Success, html);
        return m.Value;
    }

    [Fact]
    public async Task Input_with_Type_password_renders_type_password()
    {
        var html = await Render.Html<InputAtom>(new { Label = "Local password", Name = "password", Type = "password" });
        Assert.Matches("\\stype=\"password\"", InputTag(html));
    }

    [Fact]
    public async Task Input_without_Type_renders_no_type_attribute()
    {
        var html = await Render.Html<InputAtom>(new { Label = "Title", Name = "title" });
        Assert.DoesNotMatch("\\stype=", InputTag(html));
    }
}

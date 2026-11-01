using Gallery.Web.Seed;

namespace Gallery.Web.Tests;

/// <summary>
/// T029: ManifestLoader tests.
/// </summary>
public class ManifestTests
{
    private static string WriteManifest(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"manifest-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Valid_manifest_returns_no_errors()
    {
        var path = WriteManifest("""
            {
              "galleries": [
                { "key": "sep-05", "title": "September 5", "visibility": "public", "photos": ["a.jpg"] }
              ],
              "photos": { "a.jpg": { "title": "First", "caption": null } }
            }
            """);
        var (manifest, errors) = ManifestLoader.Load(path);
        Assert.NotNull(manifest);
        Assert.Empty(errors);
    }

    /// <summary>Constitution II.6: a gallery must hold at least one image to be public.</summary>
    [Fact]
    public void Public_gallery_with_no_photos_is_an_error()
    {
        var path = WriteManifest("""
            { "galleries": [ { "key": "coming-soon", "title": "Coming soon", "visibility": "public", "photos": [] } ], "photos": {} }
            """);
        var (_, errors) = ManifestLoader.Load(path);
        var e = Assert.Single(errors);
        Assert.Contains("coming-soon", e);
        Assert.Contains("at least one photo", e);
    }

    [Fact]
    public void Private_gallery_with_no_photos_is_allowed()
    {
        var path = WriteManifest("""
            { "galleries": [ { "key": "draft", "visibility": "private", "photos": [] } ], "photos": {} }
            """);
        var (manifest, errors) = ManifestLoader.Load(path);
        Assert.NotNull(manifest);
        Assert.Empty(errors);
    }

    [Fact]
    public void Missing_visibility_defaults_to_private()
    {
        var path = WriteManifest("""
            { "galleries": [ { "key": "sep-05", "photos": [] } ], "photos": {} }
            """);
        var (manifest, errors) = ManifestLoader.Load(path);
        Assert.NotNull(manifest);
        Assert.Empty(errors);
        var g = manifest!.Galleries[0];
        Assert.Equal("private", ManifestLoader.EffectiveVisibility(g));
    }

    [Fact]
    public void Null_title_defaults_to_filename_without_extension()
    {
        var photo = new ManifestPhoto { Title = null };
        var title = ManifestLoader.EffectiveTitle("DSCF1234.jpg", photo);
        Assert.Equal("DSCF1234", title);
    }

    [Fact]
    public void Null_photo_entry_defaults_to_filename_without_extension()
    {
        var title = ManifestLoader.EffectiveTitle("DSCF1234.jpg", null);
        Assert.Equal("DSCF1234", title);
    }

    [Fact]
    public void Duplicate_key_is_an_error()
    {
        var path = WriteManifest("""
            {
              "galleries": [
                { "key": "sep-05", "photos": [] },
                { "key": "sep-05", "photos": [] }
              ],
              "photos": {}
            }
            """);
        var (_, errors) = ManifestLoader.Load(path);
        Assert.Contains(errors, e => e.Contains("sep-05") && e.Contains("duplicate"));
    }

    [Fact]
    public void Invalid_visibility_is_an_error()
    {
        var path = WriteManifest("""
            {
              "galleries": [ { "key": "sep-05", "visibility": "secret", "photos": [] } ],
              "photos": {}
            }
            """);
        var (_, errors) = ManifestLoader.Load(path);
        Assert.Contains(errors, e => e.Contains("visibility"));
    }

    [Fact]
    public void Cover_not_in_photos_is_an_error()
    {
        var path = WriteManifest("""
            {
              "galleries": [ { "key": "sep-05", "cover": "missing.jpg", "photos": ["a.jpg"] } ],
              "photos": {}
            }
            """);
        var (_, errors) = ManifestLoader.Load(path);
        Assert.Contains(errors, e => e.Contains("cover") && e.Contains("missing.jpg"));
    }

    [Fact]
    public void Photo_listed_twice_in_gallery_is_an_error()
    {
        var path = WriteManifest("""
            {
              "galleries": [ { "key": "sep-05", "photos": ["a.jpg", "a.jpg"] } ],
              "photos": {}
            }
            """);
        var (_, errors) = ManifestLoader.Load(path);
        Assert.Contains(errors, e => e.Contains("a.jpg") && e.Contains("once"));
    }

    [Fact]
    public void Title_over_200_chars_is_an_error()
    {
        var longTitle = new string('A', 201);
        var path = WriteManifest($$"""
            {
              "galleries": [ { "key": "sep-05", "title": "{{longTitle}}", "photos": [] } ],
              "photos": {}
            }
            """);
        var (_, errors) = ManifestLoader.Load(path);
        Assert.Contains(errors, e => e.Contains("title"));
    }

    [Fact]
    public void Caption_over_2000_chars_is_an_error()
    {
        var longCaption = new string('A', 2001);
        var path = WriteManifest($$"""
            {
              "galleries": [ { "key": "sep-05", "photos": ["a.jpg"] } ],
              "photos": { "a.jpg": { "caption": "{{longCaption}}" } }
            }
            """);
        var (_, errors) = ManifestLoader.Load(path);
        Assert.Contains(errors, e => e.Contains("caption") && e.Contains("2000"));
    }

    [Fact]
    public void Description_over_5000_chars_is_an_error()
    {
        var longDesc = new string('A', 5001);
        var path = WriteManifest($$"""
            {
              "galleries": [ { "key": "sep-05", "description": "{{longDesc}}", "photos": [] } ],
              "photos": {}
            }
            """);
        var (_, errors) = ManifestLoader.Load(path);
        Assert.Contains(errors, e => e.Contains("description") && e.Contains("5000"));
    }

    [Fact]
    public void All_errors_returned_at_once()
    {
        var path = WriteManifest("""
            {
              "galleries": [
                { "key": "sep-05", "visibility": "secret", "cover": "missing.jpg", "photos": ["a.jpg", "a.jpg"] },
                { "key": "sep-05", "photos": [] }
              ],
              "photos": {}
            }
            """);
        var (_, errors) = ManifestLoader.Load(path);
        // Expect: duplicate key, bad visibility, cover not in photos, photo listed twice
        Assert.True(errors.Count >= 3, $"Expected ≥3 errors, got {errors.Count}: {string.Join("; ", errors)}");
    }

    [Fact]
    public void Missing_file_returns_error()
    {
        var (_, errors) = ManifestLoader.Load("/nonexistent/path/manifest.json");
        Assert.Single(errors);
        Assert.Contains("not found", errors[0], StringComparison.OrdinalIgnoreCase);
    }
}

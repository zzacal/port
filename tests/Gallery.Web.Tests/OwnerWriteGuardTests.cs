namespace Gallery.Web.Tests;

/// <summary>
/// [SAFETY] Constitution I.4 (no web reset): the only code allowed to write the owner fields is the
/// claim in <c>Data/OwnerStore.cs</c>, plus the seed's insert-only nulls in <c>Seed/Seeder.cs</c>.
/// This is a source scan, not an HTTP test, because a second writer is a defect even before any
/// route reaches it. Any other file naming <c>claimedSubject</c>, <c>claimedAt</c> or
/// <c>claimedIp</c> fails with its path. Do not widen the allow-list to make a new writer pass.
/// </summary>
public class OwnerWriteGuardTests
{
    private static readonly string[] Fields = ["claimedSubject", "claimedAt", "claimedIp"];

    [Fact]
    public void Owner_fields_are_named_only_by_the_claim_and_the_seed_insert()
    {
        var src = Path.Combine(Render.RepoRoot, "src");
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file).Replace('\\', '/');
            if (rel.Contains("/bin/") || rel.Contains("/obj/")) continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!Fields.Any(f => line.Contains(f, StringComparison.Ordinal))) continue;

                var allowed =
                    rel == "Gallery.Web/Data/OwnerStore.cs" ||
                    (rel == "Gallery.Web/Seed/Seeder.cs" && line.Contains("SetOnInsert(", StringComparison.Ordinal));
                if (!allowed) violations.Add($"{rel}:{i + 1}: {line.Trim()}");
            }
        }

        Assert.True(violations.Count == 0,
            "Owner fields named outside OwnerStore.cs / Seeder SetOnInsert (I.4):\n" + string.Join("\n", violations));
    }
}

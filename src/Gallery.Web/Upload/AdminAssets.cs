using System.Security.Cryptography;

namespace Gallery.Web.Upload;

/// <summary>
/// 008 T033 (research R-12): content versions of the admin page script and worker, computed once
/// at startup as the first 8 hex characters of each file's SHA-256 in the web root. A missing file
/// gets the version <c>dev</c>. The page links <c>/admin-assets/upload.js?v={hash8}</c>, which the
/// static-file rule serves <c>immutable</c>.
/// </summary>
public sealed class AdminAssets
{
    public const string ScriptPath = "admin-assets/upload.js";
    public const string WorkerPath = "admin-assets/upload-worker.js";
    public const string MissingVersion = "dev";

    public AdminAssets(IWebHostEnvironment env)
    {
        ScriptVersion = VersionOf(env, ScriptPath);
        WorkerVersion = VersionOf(env, WorkerPath);
        _versions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [LibraryScriptPath] = VersionOf(env, LibraryScriptPath),
            [ReorderScriptPath] = VersionOf(env, ReorderScriptPath),
        };
    }

    public string ScriptVersion { get; }
    public string WorkerVersion { get; }

    public string ScriptUrl => $"/{ScriptPath}?v={ScriptVersion}";
    public string WorkerUrl => $"/{WorkerPath}?v={WorkerVersion}";

    // 009 T003 (contracts/components.md): the library and builder scripts, versioned once at startup
    // with the same VersionOf as upload.js. Additive: the members above keep their behaviour.
    public const string LibraryScriptPath = "admin-assets/library.js";
    public const string ReorderScriptPath = "admin-assets/reorder.js";

    private readonly Dictionary<string, string> _versions;

    /// <summary>
    /// <c>/{relPath}?v={hash8}</c> for an asset versioned at startup (<see cref="LibraryScriptPath"/>,
    /// <see cref="ReorderScriptPath"/>); a file missing at startup gets <c>dev</c>. Any other path
    /// throws, so a page can never link an asset whose version is not computed.
    /// </summary>
    public string UrlFor(string relPath) =>
        _versions.TryGetValue(relPath, out var version)
            ? $"/{relPath}?v={version}"
            : throw new ArgumentException($"Asset '{relPath}' is not versioned by AdminAssets.", nameof(relPath));

    public static string VersionOf(IWebHostEnvironment env, string path)
    {
        var file = env.WebRootFileProvider.GetFileInfo(path);
        if (!file.Exists || file.IsDirectory) return MissingVersion;
        using var stream = file.CreateReadStream();
        return Convert.ToHexStringLower(SHA256.HashData(stream))[..8];
    }
}

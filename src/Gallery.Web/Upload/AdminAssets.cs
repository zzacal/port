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
    }

    public string ScriptVersion { get; }
    public string WorkerVersion { get; }

    public string ScriptUrl => $"/{ScriptPath}?v={ScriptVersion}";
    public string WorkerUrl => $"/{WorkerPath}?v={WorkerVersion}";

    public static string VersionOf(IWebHostEnvironment env, string path)
    {
        var file = env.WebRootFileProvider.GetFileInfo(path);
        if (!file.Exists || file.IsDirectory) return MissingVersion;
        using var stream = file.CreateReadStream();
        return Convert.ToHexStringLower(SHA256.HashData(stream))[..8];
    }
}

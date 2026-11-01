namespace Gallery.Web.Upload;

/// <summary>
/// The 008 upload pipeline's single registration seam (tasks.md T004, plan item 5).
/// AppBuilder calls <see cref="AddUpload"/> once and AdminEndpoints calls <see cref="MapUpload"/>
/// once on the protected /admin group, before the H6 catch-all, so every upload route inherits
/// <c>RequireAuthorization("owner")</c> and <c>AdminAntiforgeryFilter</c>.
/// Later batches add to this file only: one registration line or one route-group call per change.
/// </summary>
public static class UploadSetup
{
    public static IServiceCollection AddUpload(this IServiceCollection s, IConfiguration c)
    {
        s.AddSingleton(UploadOptions.Load(c));
        s.AddScoped<BatchStore>();
        s.AddScoped<ConfirmService>();
        s.AddSingleton<AdminAssets>();
        return s;
    }

    public static RouteGroupBuilder MapUpload(this RouteGroupBuilder admin)
    {
        admin.MapUploadEndpoints();
        return admin;
    }
}

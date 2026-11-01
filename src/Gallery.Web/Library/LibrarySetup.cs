namespace Gallery.Web.Library;

/// <summary>
/// The 009 library and gallery management registration seam (tasks.md T003).
/// AppBuilder calls <see cref="AddLibrary"/> once and AdminEndpoints calls <see cref="MapLibrary"/>
/// once on the protected /admin group, after MapUpload and before the H6 catch-all, so every 009
/// route inherits <c>RequireAuthorization("owner")</c> and <c>AdminAntiforgeryFilter</c>.
/// Later batches add to this file only: one registration line or one route-group call per change.
/// </summary>
public static class LibrarySetup
{
    public static IServiceCollection AddLibrary(this IServiceCollection s, IConfiguration c)
    {
        return s;
    }

    public static RouteGroupBuilder MapLibrary(this RouteGroupBuilder admin)
    {
        return admin;
    }
}

using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gallery.Web.Library;

/// <summary>
/// The 009 library and gallery service registration seam (tasks.md T003). AppBuilder calls
/// <see cref="AddLibrary"/> once. The owner's routes are the workspace's (020: <c>MapWorkspace</c>,
/// <c>MapWorkspaceApi</c>), mapped by AdminEndpoints on the protected /admin group.
/// </summary>
public static class LibrarySetup
{
    public static IServiceCollection AddLibrary(this IServiceCollection s, IConfiguration c)
    {
        s.AddSingleton(LibraryOptions.Load(c));
        s.AddScoped<SiteProjector>();
        s.AddScoped<MembershipRepair>();
        s.AddScoped<GalleryService>();
        s.AddScoped<ImageService>();
        s.TryAddScoped<IObjectDeleter, ImageStoreObjectDeleter>(); // TryAdd: a test `configure` replacement wins
        s.AddScoped<ImageDeleteService>();
        s.AddScoped<LibraryReader>();
        s.AddScoped<Gallery.Web.Workspace.WorkspaceReader>(); // 020 T034
        return s;
    }
}

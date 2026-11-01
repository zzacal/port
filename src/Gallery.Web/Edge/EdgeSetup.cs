namespace Gallery.Web.Edge;

/// <summary>
/// The 009 edge purge/warm registration seam (tasks.md T003). AppBuilder calls <see cref="AddEdge"/>
/// once. Later batches add to this file only: one registration line per change.
/// </summary>
public static class EdgeSetup
{
    public static IServiceCollection AddEdge(this IServiceCollection s, IConfiguration c)
    {
        return s;
    }
}

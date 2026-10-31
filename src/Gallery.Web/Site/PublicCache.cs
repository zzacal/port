using Microsoft.AspNetCore.OutputCaching;

namespace Gallery.Web.Site;

/// <summary>
/// Output-cache configuration for public pages (contracts/http.md FR-028).
/// </summary>
public static class PublicCache
{
    public const string HtmlPolicy = "public, max-age=0, s-maxage=86400";
    public const string ShortPolicy = "public, max-age=0, s-maxage=300";

    /// <summary>Output-cache policy name for public pages: 60 s, vary by path.</summary>
    public const string PolicyName = "public-page";

    /// <summary>
    /// Registers AddOutputCache with the public-page policy. Call before app.Build().
    /// </summary>
    public static IServiceCollection AddPublicOutputCache(this IServiceCollection services)
    {
        services.AddOutputCache(options =>
        {
            options.AddPolicy(PolicyName, builder =>
                builder.Expire(TimeSpan.FromSeconds(60))
                       .SetVaryByRouteValue("{**catch-all}")
                       .Tag(PolicyName));
        });
        return services;
    }
}

using Microsoft.AspNetCore.Antiforgery;

namespace Gallery.Web.Admin;

/// <summary>
/// Endpoint filter that validates the anti-forgery token on all non-safe HTTP methods.
/// Safe methods (GET, HEAD, OPTIONS, TRACE) are exempt (R-04).
/// The POST /admin/signin endpoint calls IAntiforgery.IsRequestValidAsync itself and is
/// mapped outside this filter group (T014).
/// </summary>
public sealed class AdminAntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "HEAD", "OPTIONS", "TRACE"
    };

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        if (!SafeMethods.Contains(httpContext.Request.Method))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(httpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.StatusCode(400);
            }
        }

        return await next(context);
    }
}

using Gallery.Web.Observability;
using Gallery.Web.Styles;
using Gallery.Web.Ui;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o => o.JsonWriterOptions = new() { Indented = false });

builder.Services.AddRazorComponents();
builder.Services.AddSingleton<InlineStyles>();
builder.Services.AddSingleton<SampleCatalog>();

var app = builder.Build();

app.UseRouting();
app.UseMiddleware<RequestTimingMiddleware>();

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.Context.Request.Path.StartsWithSegments("/samples"))
            ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    },
});

app.MapGet("/", () => "Hello World!");

if (app.Environment.IsDevelopment())
{
    // Load fixtures eagerly so startup timing is logged once, not on the first /ui request.
    _ = app.Services.GetRequiredService<InlineStyles>();
    _ = app.Services.GetRequiredService<SampleCatalog>();
    app.MapUi();
}

app.Run();

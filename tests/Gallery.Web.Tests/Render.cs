using Gallery.Web.Styles;
using Gallery.Web.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gallery.Web.Tests;

/// <summary>Renders Razor components to HTML in-process with the built-in HtmlRenderer.</summary>
public static class Render
{
    public static readonly string RepoRoot = FindRepoRoot();
    public static readonly string WebRoot = Path.Combine(RepoRoot, "src", "Gallery.Web", "wwwroot");

    private static readonly Lazy<IServiceProvider> Services = new(Build);

    public static SampleCatalog Samples => Services.Value.GetRequiredService<SampleCatalog>();

    public static Task<string> Html<T>(object? parameters = null) where T : IComponent
        => Html(typeof(T), parameters);

    public static async Task<string> Html(Type componentType, object? parameters = null)
    {
        await using var renderer = new HtmlRenderer(Services.Value, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = parameters is null
                ? ParameterView.Empty
                : ParameterView.FromDictionary(ToDictionary(parameters));
            var output = await renderer.RenderComponentAsync(componentType, view);
            return output.ToHtmlString();
        });
    }

    private static Dictionary<string, object?> ToDictionary(object parameters)
        => parameters as Dictionary<string, object?>
           ?? parameters.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(parameters));

    private static IServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddSingleton<IWebHostEnvironment>(new TestEnv(WebRoot));
        services.AddSingleton<InlineStyles>();
        services.AddSingleton<SampleCatalog>();
        return services.BuildServiceProvider();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Gallery.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Gallery.slnx not found above " + AppContext.BaseDirectory);
    }

    private sealed class TestEnv(string webRoot) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = webRoot;
        public IFileProvider WebRootFileProvider { get; set; } = new PhysicalFileProvider(webRoot);
        public string ApplicationName { get; set; } = "Gallery.Web";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = webRoot;
        public string EnvironmentName { get; set; } = "Development";
    }
}

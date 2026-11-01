#:sdk Microsoft.NET.Sdk.Web
#:property PublishAot=false
// Probe 008-P8: is per-endpoint RequestSizeLimitAttribute metadata honoured for minimal-API JSON
// endpoints (Kestrel), and what status does an oversized body produce?
using Microsoft.AspNetCore.Mvc;
var b = WebApplication.CreateBuilder(args);
b.WebHost.UseUrls("http://127.0.0.1:18190");
b.Logging.ClearProviders();
var app = b.Build();
app.MapPost("/small", (Payload p) => Results.Ok(p.Items.Length)).WithMetadata(new RequestSizeLimitAttribute(1024));
app.MapPost("/default", (Payload p) => Results.Ok(p.Items.Length));
await app.StartAsync();
var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:18190") };
async Task Try(string path, int n)
{
    var body = System.Text.Json.JsonSerializer.Serialize(new { items = Enumerable.Repeat("x", n).ToArray() });
    var r = await http.PostAsync(path, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
    Console.WriteLine($"{path} body={body.Length}B -> {(int)r.StatusCode}");
}
await Try("/small", 10); await Try("/small", 1000); await Try("/default", 1000);
await app.StopAsync();
record Payload(string[] Items);

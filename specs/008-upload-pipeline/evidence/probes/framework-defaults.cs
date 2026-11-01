#:sdk Microsoft.NET.Sdk.Web
#:property PublishAot=false
// Probe 008-P5: built-in static-file content types for vendored worker assets, Kestrel's default
// request-body limit, and the default antiforgery header behaviour are framework facts the plan relies on.
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Server.Kestrel.Core;
var p = new FileExtensionContentTypeProvider();
foreach (var ext in new[] { ".js", ".mjs", ".wasm", ".avif", ".webp", ".map" })
    Console.WriteLine($"{ext} -> {(p.TryGetContentType("x" + ext, out var ct) ? ct : "(none: not served)")}");
Console.WriteLine($"Kestrel MaxRequestBodySize default = {new KestrelServerLimits().MaxRequestBodySize}");
Console.WriteLine($"FormOptions MultipartBodyLengthLimit default = {new Microsoft.AspNetCore.Http.Features.FormOptions().MultipartBodyLengthLimit}");

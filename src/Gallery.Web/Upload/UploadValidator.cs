using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Gallery.Core.Images;

namespace Gallery.Web.Upload;

/// <summary>Verdict on one staged size (research R-07). <c>Reason</c> is "{storedName}: {reason}".</summary>
public sealed record SizeVerdict(
    bool Ok, bool Reader1Ok, bool Reader2Ok, string? Format, int Width, int Height, string? Sha256Hex16, string? Reason);

/// <summary>Verdict on the placeholder. <c>Bytes</c> are the decoded WebP when <c>Ok</c>.</summary>
public sealed record PlaceholderVerdict(
    bool Ok, bool Reader1Ok, bool Reader2Ok, int Width, int Height, byte[]? Bytes, string? Reason);

/// <summary>
/// The combined validator (research R-07, plan item 1) [SAFETY]. BOTH readers run every time:
/// reader 1 is <see cref="ContainerParser.Parse"/>, reader 2 is <see cref="DerivativeValidator.Validate"/>
/// CALLED unchanged. A file passes only when both readers pass and agree with the stored name and the
/// master's upright shape. No IO.
/// </summary>
public static partial class UploadValidator
{
    public const string PlaceholderName = "placeholder";
    public const string DataUriPrefix = "data:image/webp;base64,";

    [GeneratedRegex("^#[0-9a-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();

    public static SizeVerdict ValidateSize(byte[] bytes, string storedName, int expectedWidth, int masterW, int masterH)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var ext = Path.GetExtension(storedName).TrimStart('.').ToLowerInvariant();

        var r1 = ContainerParser.Parse(bytes);

        ValidationResult r2;
        using (var s = new MemoryStream(bytes, writable: false))
            r2 = DerivativeValidator.Validate(s, ext, expectedWidth, masterW / (double)masterH);

        var sha = Sha256Hex16(bytes);

        // Reader 1 and its agreement checks first: their reasons are fixed strings. Reader 2's
        // reason is used only when reader 1 and agreement all pass.
        string? reason = null;
        if (!r1.Ok) reason = r1.Reason;
        else if (r1.Format != ext) reason = "format does not match the file name";
        else if (r1.Width != expectedWidth) reason = "width does not match the file name";
        else if (Math.Abs(r1.Height - Ladder.HeightFor(expectedWidth, masterW, masterH)) > 2)
            reason = "height does not match the original's shape";
        else if (!r2.IsValid) reason = r2.Reason;

        return new SizeVerdict(
            reason is null, r1.Ok, r2.IsValid, r1.Format, r1.Width, r1.Height, sha,
            reason is null ? null : $"{storedName}: {reason}");
    }

    public static PlaceholderVerdict ValidatePlaceholder(string color, string dataUri, UploadOptions o)
    {
        static PlaceholderVerdict Fail(string reason, bool r1 = false, bool r2 = false, int w = 0, int h = 0) =>
            new(false, r1, r2, w, h, null, $"{PlaceholderName}: {reason}");

        if (color is null || !ColorPattern().IsMatch(color))
            return Fail("colour is not #rrggbb");
        if (dataUri is null || !dataUri.StartsWith(DataUriPrefix, StringComparison.Ordinal))
            return Fail("not a WebP data URI");

        var b64 = dataUri.AsSpan(DataUriPrefix.Length);
        // Bound the work before decoding: base64 of N bytes is ceil(N/3)*4 characters.
        if (b64.Length > (o.PlaceholderMaxBytes + 2) / 3 * 4)
            return Fail("over the size limit");
        var buffer = new byte[b64.Length / 4 * 3 + 3];
        if (!Convert.TryFromBase64Chars(b64, buffer, out var n))
            return Fail("not valid base64");
        if (n > o.PlaceholderMaxBytes)
            return Fail("over the size limit");
        var bytes = buffer.AsSpan(0, n).ToArray();

        var r1 = ContainerParser.Parse(bytes);
        // Reader 2 runs every time. With reader 1's dimensions when it has them, so its own
        // directory allowlist is the independent check (R-07); otherwise a nominal 1:1 call.
        bool haveDims = r1.Ok && r1.Width > 0 && r1.Height > 0;
        ValidationResult r2;
        using (var s = new MemoryStream(bytes, writable: false))
            r2 = DerivativeValidator.Validate(s, "webp",
                haveDims ? r1.Width : 1, haveDims ? r1.Width / (double)r1.Height : 1d);

        if (!r1.Ok) return Fail(r1.Reason ?? "unreadable", false, r2.IsValid);
        if (!r2.IsValid) return Fail(r2.Reason ?? "unreadable", true, false, r1.Width, r1.Height);
        if (r1.Format != "webp") return Fail("not WebP", true, true, r1.Width, r1.Height);
        if (r1.Width < 1 || r1.Height < 1 || Math.Max(r1.Width, r1.Height) > o.PlaceholderMaxEdge)
            return Fail("over the edge limit", true, true, r1.Width, r1.Height);

        return new PlaceholderVerdict(true, true, true, r1.Width, r1.Height, bytes, null);
    }

    private static string Sha256Hex16(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];
}

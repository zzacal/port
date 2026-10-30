using System.Globalization;

namespace Gallery.Core.Observability;

public static class ServerTiming
{
    public static string Format(string name, double ms) =>
        $"{name};dur={ms.ToString("0.##", CultureInfo.InvariantCulture)}";
}

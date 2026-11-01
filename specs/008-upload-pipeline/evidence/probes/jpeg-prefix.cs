#:package MetadataExtractor@2.9.3
#:property PublishAot=false
// Probe 008-P6: does MetadataExtractor read EXIF orientation and the SOF size from a JPEG PREFIX
// (as the server gets from a ranged read of the first 256 KB), and what happens when SOF is cut off?
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;

foreach (var path in args)
{
    var all = File.ReadAllBytes(path);
    foreach (var n in new[] { all.Length, Math.Min(all.Length, 262144), 600, 200 })
    {
        var prefix = all.AsSpan(0, Math.Min(n, all.Length)).ToArray();
        try
        {
            var dirs = ImageMetadataReader.ReadMetadata(new MemoryStream(prefix));
            var jpeg = dirs.OfType<JpegDirectory>().FirstOrDefault();
            var ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
            int? w = jpeg?.TryGetInt32(JpegDirectory.TagImageWidth, out var ww) == true ? ww : null;
            int? h = jpeg?.TryGetInt32(JpegDirectory.TagImageHeight, out var hh) == true ? hh : null;
            int? o = ifd0?.TryGetInt32(ExifDirectoryBase.TagOrientation, out var oo) == true ? oo : null;
            var errs = string.Join(";", dirs.SelectMany(d => d.Errors));
            Console.WriteLine($"{Path.GetFileName(path)} bytes={prefix.Length}/{all.Length}: sof={w}x{h} orientation={o} dirs=[{string.Join("|", dirs.Select(d => d.Name))}] errors=[{errs}]");
        }
        catch (Exception ex) { Console.WriteLine($"{Path.GetFileName(path)} bytes={prefix.Length}: THROW {ex.GetType().Name}: {ex.Message}"); }
    }
}

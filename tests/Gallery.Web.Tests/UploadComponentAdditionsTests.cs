using Gallery.Web.Components;
using Gallery.Web.Components.Molecules;
using Gallery.Web.Components.Organisms;

namespace Gallery.Web.Tests;

/// <summary>
/// 008 T032 (contracts/components.md C1, C2): the optional parameters added to the 005
/// <see cref="UploadDropzone"/> and <see cref="BatchItem"/>. Their defaults must render the pre-008
/// output byte for byte; the literals below were captured from the untouched components at 04ab3a8.
/// </summary>
public class UploadComponentAdditionsTests
{
    // Pre-008 renders (scope attributes come from CSS isolation and are stable per component).
    private const string DropzoneDefaultBefore =
        "<div class=\"dropzone state-idle\" b-c9xoxofch6><label class=\"dz-target\" b-c9xoxofch6><input type=\"file\" class=\"dz-input\" name=\"files\" multiple accept=\"image/jpeg\" b-c9xoxofch6 /><span class=\"dz-title\" b-c9xoxofch6>Drop JPEG photos here, or <u b-c9xoxofch6>choose files</u></span><span class=\"dz-limits\" b-c9xoxofch6>JPEG only · up to 20 MB each</span></label></div>";

    private const string DropzoneRefusedBefore =
        "<div class=\"dropzone state-refused\" b-c9xoxofch6><label class=\"dz-target\" b-c9xoxofch6><input type=\"file\" class=\"dz-input\" name=\"files\" multiple accept=\"image/jpeg\" form=\"f\" b-c9xoxofch6 /><span class=\"dz-title\" b-c9xoxofch6>Drop JPEG photos here, or <u b-c9xoxofch6>choose files</u></span><span class=\"dz-limits\" b-c9xoxofch6>JPEG only · up to 10 MB each</span></label><ul class=\"dz-refusals\" aria-live=\"polite\" b-c9xoxofch6><li b-c9xoxofch6><span class=\"fname\" title=\"x.png\" b-c9xoxofch6>x.png</span> — not a JPEG</li></ul></div>";

    // ── C1 UploadDropzone.MaxPhotos ──────────────────────────────────────────

    [Fact]
    public async Task Dropzone_MaxPhotos_100_renders_the_FR002_line()
    {
        var html = await Render.Html<UploadDropzone>(new { MaxPhotos = (int?)100 });
        Assert.Contains(
            "<span class=\"dz-limits\" b-c9xoxofch6>JPEG only · up to 20 MB each · up to 100 photos per batch</span>",
            html);
    }

    [Fact]
    public async Task Dropzone_default_is_byte_identical_to_before()
    {
        Assert.Equal(DropzoneDefaultBefore, await Render.Html<UploadDropzone>(new { }));
        Assert.Equal(DropzoneDefaultBefore, await Render.Html<UploadDropzone>());
    }

    [Fact]
    public async Task Dropzone_refused_state_without_MaxPhotos_is_byte_identical_to_before()
    {
        var html = await Render.Html<UploadDropzone>(new
        {
            State = DropzoneState.Refused,
            Refusals = (IReadOnlyList<Refusal>)[new Refusal("x.png", "not a JPEG")],
            MaxMb = 10,
            FormId = "f",
        });
        Assert.Equal(DropzoneRefusedBefore, html);
    }

    [Fact]
    public async Task Dropzone_MaxPhotos_only_changes_the_limits_line()
    {
        var html = await Render.Html<UploadDropzone>(new { MaxPhotos = (int?)100 });
        Assert.Equal(
            DropzoneDefaultBefore.Replace("up to 20 MB each</span>", "up to 20 MB each · up to 100 photos per batch</span>"),
            html);
    }
}

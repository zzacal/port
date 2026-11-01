namespace Gallery.Web.Library;

/// <summary>
/// 009 library limits, config section <c>Library</c> (research R-09, data-model "Settings"). The
/// owner pages pass these to their inputs and the services enforce the same instance, so the limit
/// the browser applies at selection time is the one the server refuses with (FR-008, FR-010).
/// </summary>
public sealed class LibraryOptions
{
    public const string Section = "Library";

    /// <summary>Library page size and the bulk-selection cap.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Image and gallery title limit; also the upload title cap
    /// (<c>ConfirmService.MaxTitleChars</c> documents the same default).</summary>
    public int TitleMax { get; set; } = 200;

    public int CaptionMax { get; set; } = 2000;

    public int DescriptionMax { get; set; } = 5000;

    /// <summary>
    /// Binds the <c>Library</c> section over the defaults and validates it. Throws
    /// <see cref="InvalidOperationException"/> naming the offending key (startup fails).
    /// </summary>
    public static LibraryOptions Load(IConfiguration c)
    {
        var o = c.GetSection(Section).Get<LibraryOptions>() ?? new LibraryOptions();
        o.Validate();
        return o;
    }

    public void Validate()
    {
        if (PageSize <= 0) throw Bad(nameof(PageSize));
        if (TitleMax <= 0) throw Bad(nameof(TitleMax));
        if (CaptionMax <= 0) throw Bad(nameof(CaptionMax));
        if (DescriptionMax <= 0) throw Bad(nameof(DescriptionMax));
    }

    private static InvalidOperationException Bad(string key) =>
        new($"Configuration {Section}:{key} must be greater than 0.");
}

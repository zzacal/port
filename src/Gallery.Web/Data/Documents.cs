using MongoDB.Bson.Serialization.Attributes;

namespace Gallery.Web.Data;

// ---------------------------------------------------------------------------
// image sub-types
// ---------------------------------------------------------------------------

public sealed class DerivativeDoc
{
    [BsonElement("w")]   public int W { get; set; }
    [BsonElement("avif")] public string Avif { get; set; } = string.Empty;
    [BsonElement("webp")] public string Webp { get; set; } = string.Empty;
    [BsonElement("bytesAvif")] public long BytesAvif { get; set; }
    [BsonElement("bytesWebp")] public long BytesWebp { get; set; }
}

public sealed class PlaceholderDoc
{
    [BsonElement("color")]   public string Color   { get; set; } = string.Empty;
    [BsonElement("preview")] public string Preview { get; set; } = string.Empty;
}

public sealed class CameraDoc
{
    [BsonElement("make")]     public string? Make     { get; set; }
    [BsonElement("model")]    public string? Model    { get; set; }
    [BsonElement("lens")]     public string? Lens     { get; set; }
    [BsonElement("focalMm")]  public decimal? FocalMm  { get; set; }
    [BsonElement("focal35Mm")] public int? Focal35Mm  { get; set; }
    [BsonElement("fNumber")]  public decimal? FNumber  { get; set; }
    [BsonElement("exposure")] public decimal? Exposure { get; set; }
    [BsonElement("iso")]      public int? Iso          { get; set; }
}

// ---------------------------------------------------------------------------
// images collection
// ---------------------------------------------------------------------------

public sealed class ImageDoc
{
    [BsonElement("_id")]         public string Id           { get; set; } = string.Empty;
    // 008 R-10 / FR-035 [SAFETY]: null (absent in BSON) for uploaded images, so the seeder's
    // "seedKey exists and not in manifest" clean-up never matches them.
    [BsonElement("seedKey")]     [BsonIgnoreIfNull] public string? SeedKey    { get; set; }
    [BsonElement("sourceHash")]  [BsonIgnoreIfNull] public string? SourceHash { get; set; }
    [BsonElement("title")]       public string Title        { get; set; } = string.Empty;
    [BsonElement("caption")]     public string? Caption     { get; set; }
    [BsonElement("width")]       public int Width           { get; set; }
    [BsonElement("height")]      public int Height          { get; set; }
    [BsonElement("placeholder")] public PlaceholderDoc Placeholder { get; set; } = new();
    [BsonElement("camera")]      public CameraDoc? Camera   { get; set; }
    [BsonElement("derivatives")] public List<DerivativeDoc> Derivatives { get; set; } = [];
    [BsonElement("galleryIds")]  public List<string> GalleryIds { get; set; } = [];
    [BsonElement("createdAt")]   public DateTime CreatedAt  { get; set; }
    // 009 R-02: tombstone set at the start of a delete; absent on live images.
    [BsonElement("deletingAt")]  [BsonIgnoreIfNull] public DateTime? DeletingAt { get; set; }
}

// ---------------------------------------------------------------------------
// galleries collection
// ---------------------------------------------------------------------------

public sealed class CoverImageDoc
{
    [BsonElement("imageId")]     public string ImageId     { get; set; } = string.Empty;
    [BsonElement("width")]       public int Width          { get; set; }
    [BsonElement("height")]      public int Height         { get; set; }
    [BsonElement("placeholder")] public PlaceholderDoc Placeholder { get; set; } = new();
    [BsonElement("derivatives")] public List<DerivativeDoc> Derivatives { get; set; } = [];
}

public sealed class GalleryCardDoc
{
    [BsonElement("galleryId")]   public string GalleryId   { get; set; } = string.Empty;
    [BsonElement("slug")]        public string Slug        { get; set; } = string.Empty;
    [BsonElement("title")]       public string Title       { get; set; } = string.Empty;
    [BsonElement("imageCount")]  public int ImageCount     { get; set; }
    [BsonElement("cover")]       public CoverImageDoc? Cover { get; set; }
}

public sealed class GalleryDoc
{
    [BsonElement("_id")]          public string Id           { get; set; } = string.Empty;
    // 009 FR-048 [SAFETY]: null (absent in BSON) for owner-created galleries, so the removal
    // command's "seedKey exists and non-empty" filter never selects them.
    [BsonElement("seedKey")]      [BsonIgnoreIfNull] public string? SeedKey { get; set; }
    [BsonElement("slug")]         public string Slug         { get; set; } = string.Empty;
    [BsonElement("title")]        public string Title        { get; set; } = string.Empty;
    [BsonElement("description")]  public string? Description { get; set; }
    [BsonElement("visibility")]   public string Visibility   { get; set; } = "private";
    [BsonElement("items")]        public List<string> Items  { get; set; } = [];
    [BsonElement("pinnedCover")]  public string? PinnedCover { get; set; }
    [BsonElement("card")]         public GalleryCardDoc? Card { get; set; }
    [BsonElement("updatedAt")]    public DateTime UpdatedAt  { get; set; }
}

// ---------------------------------------------------------------------------
// site collection (single document _id="site")
// ---------------------------------------------------------------------------

public sealed class SiteDoc
{
    [BsonElement("_id")]         public string Id          { get; set; } = "site";
    [BsonElement("homeOrder")]   public List<string> HomeOrder { get; set; } = [];
    [BsonElement("homeCards")]   public List<GalleryCardDoc> HomeCards { get; set; } = [];
    [BsonElement("updatedAt")]   public DateTime UpdatedAt { get; set; }
}

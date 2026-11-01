using MongoDB.Bson.Serialization.Attributes;

namespace Gallery.Web.Data;

// ---------------------------------------------------------------------------
// 008 batches collection (data-model.md "Upload batch", "Batch item").
// BSON names follow data-model.md exactly. The placeholder reuses the existing
// PlaceholderDoc ({color, preview}) and the camera the existing CameraDoc, so
// the shapes that become ImageDoc fields on confirm are the same types.
// ---------------------------------------------------------------------------

/// <summary>Stored batch states. <c>confirming</c> is open + confirmStartedAt, never stored.</summary>
public static class BatchStates
{
    public const string Open = "open";
    public const string Confirmed = "confirmed";
    public const string Discarded = "discarded";
}

/// <summary>Stored item states. <c>interrupted</c> is computed on read, never stored.</summary>
public static class ItemStates
{
    public const string Queued = "queued";
    public const string Processing = "processing";
    public const string Uploading = "uploading";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Interrupted = "interrupted";
}

public sealed class BatchDoc
{
    [BsonElement("_id")]       public string Id     { get; set; } = string.Empty;
    [BsonElement("state")]     public string State  { get; set; } = BatchStates.Open;
    [BsonElement("items")]     public List<BatchItemDoc> Items { get; set; } = [];
    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; }
    [BsonElement("updatedAt")] public DateTime UpdatedAt { get; set; }

    // Omitted while unset. A Mongo filter `confirmStartedAt: null` matches the missing field.
    [BsonElement("confirmStartedAt")] [BsonIgnoreIfNull] public DateTime? ConfirmStartedAt { get; set; }
    [BsonElement("confirmedAt")]      [BsonIgnoreIfNull] public DateTime? ConfirmedAt      { get; set; }
    [BsonElement("discardedAt")]      [BsonIgnoreIfNull] public DateTime? DiscardedAt      { get; set; }
    [BsonElement("confirmedCount")]   [BsonIgnoreIfNull] public int?      ConfirmedCount   { get; set; }
}

public sealed class BatchItemDoc
{
    [BsonElement("itemId")]      public string ItemId    { get; set; } = string.Empty;
    [BsonElement("fileName")]    public string FileName  { get; set; } = string.Empty;
    [BsonElement("fileSize")]    public long FileSize    { get; set; }
    [BsonElement("state")]       public string State     { get; set; } = ItemStates.Queued;
    [BsonElement("percent")]     public int Percent      { get; set; }
    [BsonElement("reason")]      public string? Reason   { get; set; }
    [BsonElement("attemptId")]   public string AttemptId { get; set; } = string.Empty;
    [BsonElement("heartbeatAt")] public DateTime HeartbeatAt { get; set; }
    [BsonElement("master")]      public MasterDoc? Master { get; set; }
    [BsonElement("stored")]      public List<string> Stored { get; set; } = [];
    [BsonElement("validated")]   public ValidatedDoc? Validated { get; set; }
}

/// <summary>The stored original as the master check read it. Width/height are UPRIGHT.</summary>
public sealed class MasterDoc
{
    [BsonElement("length")] public long Length   { get; set; }
    [BsonElement("etag")]   public string Etag   { get; set; } = string.Empty;
    [BsonElement("width")]  public int Width     { get; set; }
    [BsonElement("height")] public int Height    { get; set; }
}

/// <summary>Set only when the item becomes succeeded (L-01).</summary>
public sealed class ValidatedDoc
{
    [BsonElement("width")]       public int Width  { get; set; }
    [BsonElement("height")]      public int Height { get; set; }
    [BsonElement("placeholder")] public PlaceholderDoc Placeholder { get; set; } = new();
    [BsonElement("camera")]      public CameraDoc? Camera { get; set; }
    [BsonElement("sizes")]       public List<ValidatedSizeDoc> Sizes { get; set; } = [];
    [BsonElement("masterEtag")]  public string MasterEtag { get; set; } = string.Empty;
}

public sealed class ValidatedSizeDoc
{
    [BsonElement("w")]         public int W            { get; set; }
    [BsonElement("avifKey")]   public string AvifKey   { get; set; } = string.Empty;
    [BsonElement("webpKey")]   public string WebpKey   { get; set; } = string.Empty;
    [BsonElement("avifEtag")]  public string AvifEtag  { get; set; } = string.Empty;
    [BsonElement("webpEtag")]  public string WebpEtag  { get; set; } = string.Empty;
    [BsonElement("bytesAvif")] public long BytesAvif   { get; set; }
    [BsonElement("bytesWebp")] public long BytesWebp   { get; set; }
}

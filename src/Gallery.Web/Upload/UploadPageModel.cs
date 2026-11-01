using Gallery.Web.Components;
using Gallery.Web.Data;

namespace Gallery.Web.Upload;

/// <summary>
/// 008 C3 view model for <c>UploadPage.razor</c>. The limits come from the SAME
/// <see cref="UploadOptions"/> instance the server enforces with (FR-002). It carries no storage
/// URL, key or presigned URL (FR-021, FR-043).
/// </summary>
public sealed record UploadPageModel(
    UploadBatchView? Batch,
    long MaxFileBytes,
    long MaxMb,
    int MaxPhotos,
    int? ConfirmedCount,
    string CsrfToken,
    string ScriptUrl,
    string WorkerUrl,
    string StateUrl = UploadPageModel.DefaultStateUrl)
{
    public const string DefaultStateUrl = "/admin/upload/state";
    public const string FormId = "upload-actions";

    public static UploadPageModel Create(
        BatchDoc? openBatch, int? confirmedCount, UploadOptions options, AdminAssets assets,
        string csrfToken, DateTimeOffset now) =>
        new(
            openBatch is null ? null : UploadBatchView.From(openBatch, options, now),
            options.MaxFileBytes,
            options.MaxMb,
            options.MaxPhotosPerBatch,
            confirmedCount,
            csrfToken,
            assets.ScriptUrl,
            assets.WorkerUrl);
}

/// <summary>The open batch as the page shows it: every item in its EFFECTIVE state.</summary>
public sealed record UploadBatchView(string Id, IReadOnlyList<UploadItemView> Items)
{
    public static UploadBatchView From(BatchDoc batch, UploadOptions options, DateTimeOffset now) =>
        new(batch.Id, batch.Items.Select(i => new UploadItemView(
            i.ItemId, i.FileName, i.FileSize,
            ToComponentState(EffectiveState.Of(i, now, options.HeartbeatTimeout)),
            i.Percent, i.Reason)).ToList());

    public int Total => Items.Count;
    public int Ready => Items.Count(i => i.State == BatchItemState.Succeeded);
    public int Failed => Items.Count(i => i.State is BatchItemState.Failed or BatchItemState.Interrupted);

    /// <summary>contracts/components.md C3 footer mapping (decisions row 26).</summary>
    public BatchState FooterState =>
        Total > 0 && Ready == Total ? BatchState.Ready
        : Failed > 0 ? BatchState.Blocked
        : BatchState.InProgress;

    public static BatchItemState ToComponentState(string effective) => effective switch
    {
        ItemStates.Queued => BatchItemState.Queued,
        ItemStates.Processing => BatchItemState.Processing,
        ItemStates.Uploading => BatchItemState.Uploading,
        ItemStates.Succeeded => BatchItemState.Succeeded,
        ItemStates.Failed => BatchItemState.Failed,
        ItemStates.Interrupted => BatchItemState.Interrupted,
        _ => throw new ArgumentOutOfRangeException(nameof(effective), effective, "Unknown item state."),
    };
}

public sealed record UploadItemView(
    string ItemId, string FileName, long FileSize, BatchItemState State, int Percent, string? Reason);

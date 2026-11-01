using Gallery.Web.Data;

namespace Gallery.Web.Upload;

/// <summary>
/// The EFFECTIVE item state (data-model "Effective state", Clarification 4, FR-010):
/// <c>interrupted</c> when the stored state is queued, processing or uploading and
/// <c>now − heartbeatAt &gt; timeout</c>; otherwise the stored state. Pure; never persisted.
/// </summary>
public static class EffectiveState
{
    public static string Of(BatchItemDoc item, DateTimeOffset now, TimeSpan timeout)
    {
        var active = item.State is ItemStates.Queued or ItemStates.Processing or ItemStates.Uploading;
        if (!active) return item.State;

        var heartbeat = new DateTimeOffset(DateTime.SpecifyKind(item.HeartbeatAt, DateTimeKind.Utc));
        return now - heartbeat > timeout ? ItemStates.Interrupted : item.State;
    }
}

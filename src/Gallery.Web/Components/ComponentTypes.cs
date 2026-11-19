namespace Gallery.Web.Components;

// View-state inputs shared by components across levels (feature 005, contracts/admin-components.md).
// Every transient state is a parameter so /ui/ can render it statically.

public enum ButtonVariant { Primary, Secondary, Quiet, Danger }

public enum ButtonElement { Button, Link, Summary }

public enum SaveState { Idle, Saving, Saved }

public enum DropzoneState { Idle, DragOver, Refused }

public enum SignInState { SignIn, Claim, Refused }

public sealed record Refusal(string FileName, string Reason);

public sealed record FormToken(string FieldName, string Value);

/// <summary>Feature 020 K7: one upload row in the workspace tray. Item states map queued → Waiting,
/// processing → Preparing, uploading → Uploading, succeeded|joining → Adding, failed → Failed,
/// interrupted → Interrupted; Added once the photo has joined.</summary>
public enum UploadRowState { Waiting, Preparing, Uploading, Adding, Added, Failed, Interrupted }

/// <summary>Feature 020 K10: the workspace upload tray. Empty (nothing picked yet), Working (rows in
/// progress), Hidden (collapsed to its heading line), Finished, FinishedWithFailures.</summary>
public enum TrayState { Empty, Working, Hidden, Finished, FinishedWithFailures }

/// <summary>Feature 020 K10: one row of an <c>UploadTray</c> (the parameters of its <c>UploadRow</c>).</summary>
public sealed record UploadTrayRow(string FileName, UploadRowState State, int Percent = 0, string? Reason = null, string? ItemId = null);

/// <summary>Feature 020 K9: one gallery row of a <c>CollectionList</c>, in home page order.</summary>
public sealed record CollectionGallery(string Id, string Slug, string Title, bool Public, int Count);

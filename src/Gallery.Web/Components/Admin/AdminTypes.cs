namespace Gallery.Web.Components.Admin;

// View-state inputs for the admin components (feature 005, contracts/admin-components.md).
// Every transient state is a parameter so /ui/ can render it statically.

public enum ButtonVariant { Primary, Secondary, Quiet, Danger }

public enum ButtonElement { Button, Link, Summary }

public enum SaveState { Idle, Saving, Saved }

public enum BatchItemState { Queued, Processing, Uploading, Succeeded, Failed, Interrupted }

public enum CoverState { Pinned, Automatic, Unpinned }

public enum HandleState { Rest, Focused, Lifted }

public enum DropzoneState { Idle, DragOver, Refused }

public enum BatchState { InProgress, Ready, Blocked, ConfirmDiscard }

public enum BuilderState { Rest, Dragging, KeyboardLifted, PublishWarning, Empty }

public enum SignInState { SignIn, Claim, Refused }

public enum AdminPage { Upload, Library, Galleries }

public sealed record Refusal(string FileName, string Reason);

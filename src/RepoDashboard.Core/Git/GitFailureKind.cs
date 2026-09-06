namespace RepoDashboard.Core.Git;

/// <summary>
/// Known Git failure families with user-friendly guidance.
/// Unknown failures carry no hint — raw output is shown as-is.
/// </summary>
public enum GitFailureKind
{
    Authentication,
    NetworkUnreachable,
    RemoteNotFound
}

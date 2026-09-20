namespace RepoDashboard.App.Services;

/// <summary>
/// Opens a folder in the platform file manager (RM-004). Windows uses the
/// shell-associated handler; Linux uses <c>xdg-open</c>. Failures throw and
/// the caller maps them to the existing user-visible status error style
/// instead of crashing.
/// </summary>
public interface IFolderLauncher
{
    void OpenFolder(string path);
}

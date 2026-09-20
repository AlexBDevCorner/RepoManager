using System.Diagnostics;

namespace RepoDashboard.App.Services;

/// <summary>
/// Cross-platform folder launcher (RM-004). Windows opens the folder via
/// the shell-associated handler; Linux uses <c>xdg-open</c>. Failures throw
/// and the caller maps them to the existing status error style.
/// </summary>
public sealed class DesktopFolderLauncher : IFolderLauncher
{
    public void OpenFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
                return;
            }

            // Linux: xdg-open is the desktop-neutral opener. UseShellExecute
            // must be false so the executable is resolved via PATH without
            // a shell; arguments use ArgumentList to avoid quoting issues.
            var startInfo = new ProcessStartInfo
            {
                FileName = "xdg-open",
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(path);

            using var process = Process.Start(startInfo);

            if (process is null)
            {
                throw new InvalidOperationException(
                    "Could not open the folder: 'xdg-open' did not start.");
            }
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new InvalidOperationException(
                $"Could not open the folder '{path}'.", ex);
        }
    }
}

namespace RepoDashboard.App.Services;

/// <summary>
/// Launches a terminal in a repository folder (RM-004). Windows prefers
/// Windows Terminal (<c>wt.exe</c>) when available; Linux prefers an
/// explicit <c>$TERMINAL</c> when usable, otherwise common terminal
/// launchers. When no supported terminal is available,
/// <see cref="IsAvailable"/> is false and the terminal action stays
/// disabled; <see cref="OpenTerminal"/> never crashes the app.
/// </summary>
public interface ITerminalLauncher
{
    bool IsAvailable { get; }

    void OpenTerminal(string workingDirectory);
}

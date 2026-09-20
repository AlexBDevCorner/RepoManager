using System.Diagnostics;

namespace RepoDashboard.App.Services;

/// <summary>
/// Cross-platform terminal launcher (RM-004). Windows prefers Windows
/// Terminal (<c>wt.exe</c>) when available; Linux prefers an explicit
/// <c>$TERMINAL</c> when usable, otherwise common terminal launchers.
/// Detection is lazy and filesystem-based (no shell). When no supported
/// terminal is found, <see cref="IsAvailable"/> is false so the UI stays
/// disabled instead of failing.
/// </summary>
public sealed class TerminalLauncher : ITerminalLauncher
{
    private readonly Lazy<bool> _isAvailable;
    private readonly Func<string?> _terminalEnvironment;
    private readonly Func<string, bool> _executableExists;

    public TerminalLauncher(
        Func<string?>? terminalEnvironment = null,
        Func<string, bool>? executableExists = null)
    {
        _terminalEnvironment = terminalEnvironment ?? (() => Environment.GetEnvironmentVariable("TERMINAL"));
        _executableExists = executableExists ?? ExecutableExistsOnPath;
        _isAvailable = new Lazy<bool>(DetectAvailability);
    }

    public bool IsAvailable => _isAvailable.Value;

    public void OpenTerminal(string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (!IsAvailable)
        {
            throw new InvalidOperationException("No supported terminal was found.");
        }

        try
        {
            var startInfo = BuildStartInfo(workingDirectory);

            using var process = Process.Start(startInfo);

            if (process is null)
            {
                throw new InvalidOperationException("The terminal process did not start.");
            }
        }
        catch (Exception ex) when (ex is not ArgumentException && ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Could not open a terminal in '{workingDirectory}'.", ex);
        }
    }

    private ProcessStartInfo BuildStartInfo(string workingDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo
            {
                FileName = "wt.exe",
                UseShellExecute = true,
                WorkingDirectory = workingDirectory
            }.WithArguments($"-d \"{workingDirectory}\"");
        }

        var terminalSpec = _terminalEnvironment();

        if (!string.IsNullOrWhiteSpace(terminalSpec))
        {
            var parts = SplitCommand(terminalSpec);

            if (parts.Count > 0 && _executableExists(parts[0]))
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = parts[0],
                    UseShellExecute = false,
                    WorkingDirectory = workingDirectory
                };

                foreach (var arg in parts.Skip(1))
                {
                    startInfo.ArgumentList.Add(arg);
                }

                return startInfo;
            }
        }

        foreach (var candidate in LinuxCandidates)
        {
            if (_executableExists(candidate))
            {
                return new ProcessStartInfo
                {
                    FileName = candidate,
                    UseShellExecute = false,
                    WorkingDirectory = workingDirectory
                };
            }
        }

        throw new InvalidOperationException("No supported terminal was found.");
    }

    private bool DetectAvailability()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return _executableExists("wt.exe");
            }

            var terminalSpec = _terminalEnvironment();

            if (!string.IsNullOrWhiteSpace(terminalSpec))
            {
                var parts = SplitCommand(terminalSpec);

                if (parts.Count > 0 && _executableExists(parts[0]))
                {
                    return true;
                }
            }

            return LinuxCandidates.Any(_executableExists);
        }
        catch
        {
            return false;
        }
    }

    private static readonly IReadOnlyList<string> LinuxCandidates =
    [
        "x-terminal-emulator",
        "gnome-terminal",
        "konsole",
        "xfce4-terminal",
        "mate-terminal",
        "alacritty",
        "kitty",
        "xterm",
    ];

    private static List<string> SplitCommand(string command)
    {
        return command
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Trim().Trim('"'))
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();
    }

    internal static bool ExecutableExistsOnPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var trimmed = fileName.Trim().Trim('"');

        try
        {
            if (Path.IsPathRooted(trimmed))
            {
                return File.Exists(trimmed);
            }

            var path = Environment.GetEnvironmentVariable("PATH");

            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var extensions = OperatingSystem.IsWindows()
                ? new[] { string.Empty, ".exe", ".cmd", ".bat" }
                : new[] { string.Empty };

            return path
                .Split(Path.PathSeparator)
                .Select(directory => directory.Trim().Trim('"'))
                .Where(directory => !string.IsNullOrWhiteSpace(directory))
                .Any(directory =>
                {
                    try
                    {
                        return extensions.Any(extension =>
                            File.Exists(Path.Combine(directory, trimmed + extension)));
                    }
                    catch
                    {
                        return false;
                    }
                });
        }
        catch
        {
            return false;
        }
    }
}

internal static class ProcessStartInfoExtensions
{
    internal static ProcessStartInfo WithArguments(this ProcessStartInfo startInfo, string arguments)
    {
        startInfo.Arguments = arguments;
        return startInfo;
    }
}

using FluentAssertions;
using RepoDashboard.App.Services;

namespace RepoDashboard.App.Tests.ViewModels;

/// <summary>
/// RM-004: terminal launching prefers Windows Terminal on Windows and
/// $TERMINAL/common launchers on Linux. No supported terminal means
/// disabled, never a crash. OS is injectable so Linux behavior is testable
/// on the Windows worker.
/// </summary>
public sealed class TerminalLauncherTests
{
    [Fact]
    public void Windows_is_available_when_wt_exe_exists()
    {
        var sut = new TerminalLauncher(
            terminalEnvironment: () => null,
            executableExists: name => string.Equals(name, "wt.exe", StringComparison.Ordinal),
            isWindows: () => true);

        sut.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void Windows_is_unavailable_without_wt_exe()
    {
        var sut = new TerminalLauncher(
            terminalEnvironment: () => null,
            executableExists: _ => false,
            isWindows: () => true);

        sut.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void Linux_prefers_terminal_environment_when_usable()
    {
        var sut = new TerminalLauncher(
            terminalEnvironment: () => "myterm",
            executableExists: name => string.Equals(name, "myterm", StringComparison.Ordinal),
            isWindows: () => false);

        sut.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void Linux_falls_back_to_common_launchers()
    {
        var sut = new TerminalLauncher(
            terminalEnvironment: () => null,
            executableExists: name => string.Equals(name, "gnome-terminal", StringComparison.Ordinal),
            isWindows: () => false);

        sut.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void Linux_is_unavailable_without_any_terminal()
    {
        var sut = new TerminalLauncher(
            terminalEnvironment: () => null,
            executableExists: _ => false,
            isWindows: () => false);

        sut.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void Linux_ignores_unusable_terminal_environment_and_uses_fallback()
    {
        var sut = new TerminalLauncher(
            terminalEnvironment: () => "missing-term",
            executableExists: name => string.Equals(name, "xterm", StringComparison.Ordinal),
            isWindows: () => false);

        sut.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void OpenTerminal_throws_clear_error_when_unavailable()
    {
        var sut = new TerminalLauncher(
            terminalEnvironment: () => null,
            executableExists: _ => false,
            isWindows: () => false);

        var act = () => sut.OpenTerminal("/tmp");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No supported terminal*");
    }

    [Fact]
    public void OpenTerminal_rejects_empty_path()
    {
        var sut = new TerminalLauncher(
            terminalEnvironment: () => null,
            executableExists: _ => true,
            isWindows: () => false);

        var act = () => sut.OpenTerminal("   ");

        act.Should().Throw<ArgumentException>();
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace RepoDashboard.App.Services;

/// <summary>
/// Avalonia clipboard implementation (RM-004). Resolves the clipboard from
/// the current desktop main window's <c>TopLevel</c>; throws a clear error
/// when no desktop session is available instead of crashing.
/// </summary>
public sealed class AvaloniaClipboardService : IClipboardService
{
    private readonly Func<Window?> _mainWindowProvider;

    public AvaloniaClipboardService(Func<Window?>? mainWindowProvider = null)
    {
        _mainWindowProvider = mainWindowProvider ?? GetMainWindow;
    }

    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var clipboard = GetClipboard();

        if (clipboard is null)
        {
            throw new InvalidOperationException("Clipboard is unavailable.");
        }

        await clipboard.SetTextAsync(text);
    }

    private IClipboard? GetClipboard()
    {
        var window = _mainWindowProvider();

        if (window is null)
        {
            return null;
        }

        return TopLevel.GetTopLevel(window)?.Clipboard;
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        return null;
    }
}

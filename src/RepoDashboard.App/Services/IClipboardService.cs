namespace RepoDashboard.App.Services;

/// <summary>
/// Clipboard access behind an interface (RM-004) so view models stay
/// testable without a real desktop session. The Avalonia implementation
/// uses the active <c>TopLevel</c> clipboard; tests use an in-memory fake.
/// </summary>
public interface IClipboardService
{
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace RepoDashboard.App.Services;

/// <summary>
/// Avalonia removal confirmation (RM-004). Shows a modal Yes/No dialog with
/// the existing wording ("files will not be deleted") and returns true only
/// on Yes. Ownership is explicit via the desktop main window.
/// </summary>
public sealed class RepositoryRemovalConfirmationService
    : IRepositoryRemovalConfirmationService
{
    private readonly Func<Window?> _mainWindowProvider;

    public RepositoryRemovalConfirmationService(Func<Window?>? mainWindowProvider = null)
    {
        _mainWindowProvider = mainWindowProvider ?? GetMainWindow;
    }

    public async Task<bool> ConfirmRemovalAsync(
        string repositoryName,
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        var owner = _mainWindowProvider();

        if (owner is null)
        {
            throw new InvalidOperationException("No main window available for the confirmation dialog.");
        }

        var dialog = new Window
        {
            Title = "Repo Dashboard",
            Width = 420,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        var text = new TextBlock
        {
            Text = $"Remove {repositoryName} from Repo Dashboard?\n\nThe repository and its files will not be deleted.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Thickness(16, 16, 16, 8)
        };

        var yesButton = new Button { Content = "Yes", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
        var noButton = new Button { Content = "No", Width = 90, IsDefault = true, IsCancel = true };

        var completion = new TaskCompletionSource<bool>();

        yesButton.Click += (_, _) => { completion.TrySetResult(true); dialog.Close(true); };
        noButton.Click += (_, _) => { completion.TrySetResult(false); dialog.Close(false); };

        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 16, 16)
        };
        buttons.Children.Add(yesButton);
        buttons.Children.Add(noButton);

        var panel = new StackPanel();
        panel.Children.Add(text);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        var result = await dialog.ShowDialog<bool?>(owner);
        return result == true;
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

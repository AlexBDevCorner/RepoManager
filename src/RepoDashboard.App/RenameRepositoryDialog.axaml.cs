using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace RepoDashboard.App;

/// <summary>
/// Edits a repository display name (alias). The current name is
/// selected on open so typing immediately replaces it. Save stays
/// disabled for empty names; the service still owns final validation.
/// The physical folder is never touched here.
/// </summary>
public partial class RenameRepositoryDialog : Window
{
    public string RepositoryName => NameBox.Text ?? string.Empty;

    public RenameRepositoryDialog(string currentName, string repositoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        InitializeComponent();

        NameBox.Text = currentName;
        PathText.Text = repositoryPath;
        UpdateSaveEnabled();

        NameBox.TextChanged += (_, _) => UpdateSaveEnabled();
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    // Parameterless constructor for Avalonia XAML previewer.
    public RenameRepositoryDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void UpdateSaveEnabled() =>
        SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text);

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }
}

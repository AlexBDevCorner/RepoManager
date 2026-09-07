using System.Windows;

namespace RepoDashboard.App;

/// <summary>
/// Edits a repository display name (Task 49 alias). The current name is
/// selected on open so typing immediately replaces it. Save stays
/// disabled for empty names; the service still owns final validation.
/// The physical folder is never touched here.
/// </summary>
public partial class RenameRepositoryDialog : Window
{
    public string RepositoryName => NameBox.Text;

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

    private void UpdateSaveEnabled() =>
        SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}

namespace RepoDashboard.App.Services;

public sealed class RepositoryNameDialogService : IRepositoryNameDialogService
{
    public string? RequestName(
        string currentName,
        string repositoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        var dialog = new RenameRepositoryDialog(currentName, repositoryPath)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true
            ? dialog.RepositoryName
            : null;
    }
}

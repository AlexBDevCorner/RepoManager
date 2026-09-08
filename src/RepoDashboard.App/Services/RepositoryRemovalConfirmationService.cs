namespace RepoDashboard.App.Services;

/// <summary>
/// Production removal confirmation: shows the existing MessageBox
/// ("files will not be deleted") and returns true only on Yes.
/// </summary>
public sealed class RepositoryRemovalConfirmationService
    : IRepositoryRemovalConfirmationService
{
    public bool ConfirmRemoval(
        string repositoryName,
        string repositoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        return System.Windows.MessageBox.Show(
            $"Remove {repositoryName} from Repo Dashboard?\n\n" +
            "The repository and its files will not be deleted.",
            "Repo Dashboard",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question)
            == System.Windows.MessageBoxResult.Yes;
    }
}

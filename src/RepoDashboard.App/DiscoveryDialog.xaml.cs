using System.Windows;
using RepoDashboard.App.ViewModels;

namespace RepoDashboard.App;

/// <summary>
/// Confirmation checklist for repository discovery (Task 40).
/// Shows candidates — nothing is added until the user presses
/// <c>Add Selected</c>. Already-tracked rows are pre-unchecked with
/// an "already on dashboard" note; the view model filters them out
/// of the result so duplicates can never be added from here.
/// </summary>
public partial class DiscoveryDialog : Window
{
    public DiscoveryDialog(DiscoveryDialogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;

        // Review #15: ListBox.InputBindings only participate when focus
        // is inside the ListBox. Focus it on Loaded (per WPF focus
        // guidance) and ensure a sensible highlight so Space / arrows /
        // Ctrl+A work immediately after open.
        Loaded += (_, _) =>
        {
            if (viewModel.SelectedOption is null)
            {
                viewModel.SelectedOption =
                    viewModel.Options.FirstOrDefault(o => o.IsSelectable)
                    ?? viewModel.Options.FirstOrDefault();
            }

            RepositoryList.Focus();
        };
    }

    private void AddSelected_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}

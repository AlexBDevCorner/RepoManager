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
    }

    private void AddSelected_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}

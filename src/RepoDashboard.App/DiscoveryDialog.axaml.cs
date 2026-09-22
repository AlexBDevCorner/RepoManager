using Avalonia.Controls;
using Avalonia.Interactivity;
using RepoDashboard.App.ViewModels;

namespace RepoDashboard.App;

/// <summary>
/// Confirmation checklist for repository discovery.
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

        Loaded += (_, _) =>
        {
            if (viewModel.SelectedOption is null)
            {
                viewModel.SelectedOption =
                    viewModel.Options.FirstOrDefault(o => o.IsSelectable)
                    ?? viewModel.Options.FirstOrDefault();
            }

            // RM-005: same namescope wiring as MainWindow. The generated
            // InitializeComponent assigns RepositoryList; fall back to an
            // explicit lookup and fail fast if the list is miswired
            // instead of throwing NullReferenceException. Store back to
            // the field so the explicit RepositoryList.Focus() contract
            // below stays valid.
            RepositoryList = RepositoryList
                ?? this.FindControl<ListBox>("RepositoryList")
                ?? throw new InvalidOperationException(
                    "DiscoveryDialog.RepositoryList could not be resolved "
                    + "after XAML initialization. Ensure "
                    + "DiscoveryDialog.axaml defines "
                    + "<ListBox x:Name=\"RepositoryList\" ...>.");

            RepositoryList.Focus();
        };
    }

    // Parameterless constructor for Avalonia XAML previewer.
    // RM-005: binds to the generated InitializeComponent(bool) which
    // wires x:Name fields; do not add a private parameterless overload
    // that would shadow it and leave RepositoryList null.
    public DiscoveryDialog()
    {
        InitializeComponent();
    }

    private void AddSelected_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }
}

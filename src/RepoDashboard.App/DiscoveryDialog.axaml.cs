using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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

            RepositoryList.Focus();
        };
    }

    // Parameterless constructor for Avalonia XAML previewer.
    public DiscoveryDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void AddSelected_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }
}

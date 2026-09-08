using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using RepoDashboard.App.ViewModels;

namespace RepoDashboard.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly DispatcherTimer _timeDisplayTimer;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // Time-derived text (relative fetch age, stale indicator) must
        // evolve while the application sits open, not just when Git runs.
        // The timer lives in the view so the view model stays
        // dispatcher-free and unit-testable.
        _timeDisplayTimer = new DispatcherTimer(
            TimeSpan.FromMinutes(1),
            DispatcherPriority.Background,
            (_, _) => _viewModel.RefreshTimeDisplays(),
            Dispatcher);

        Closed += (_, _) => _timeDisplayTimer.Stop();

        // Task 52: keyboard-first startup. The window is shown before the
        // asynchronous repository load finishes (see App.OnStartup), so
        // focus must work even while loading is still in flight. Defer
        // to Input priority so the visual tree is ready.
        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(
                () => RepositoryGrid.Focus(),
                DispatcherPriority.Input);
        };
    }

    /// <summary>
    /// Task 53: opens the keyboard shortcut reference. Purely visual, so
    /// it lives in the view — no help-dialog state in MainWindowViewModel,
    /// no extra service interface. Bound via ApplicationCommands.Help so
    /// F1 works anywhere in the window.
    /// </summary>
    private void Help_Executed(
        object sender,
        ExecutedRoutedEventArgs e)
    {
        var dialog = new KeyboardShortcutsDialog
        {
            Owner = this
        };

        dialog.ShowDialog();
    }
}

using System.Windows;
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
    }
}

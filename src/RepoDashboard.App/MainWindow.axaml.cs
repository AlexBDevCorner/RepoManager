using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using RepoDashboard.App.ViewModels;

namespace RepoDashboard.App;

/// <summary>
/// Avalonia main window (RM-004). Keeps the view thin: time-derived text
/// refreshes on a dispatcher timer so the view model stays
/// dispatcher-free and unit-testable.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel? _viewModel;
    private readonly DispatcherTimer? _timeDisplayTimer;

    /// <summary>
    /// Parameterless constructor for the Avalonia XAML loader/previewer.
    /// Production resolves via dependency injection with a view model.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        DataContext = viewModel;

        // Time-derived text (relative fetch age, stale indicator) must
        // evolve while the application sits open, not just when Git runs.
        // The timer lives in the view so the view model stays
        // dispatcher-free and unit-testable.
        _timeDisplayTimer = new DispatcherTimer(
            TimeSpan.FromMinutes(1),
            DispatcherPriority.Background,
            (_, _) => _viewModel.RefreshTimeDisplays());

        Closed += (_, _) => _timeDisplayTimer.Stop();

        // Keyboard-first startup. The window is shown before the
        // asynchronous repository load finishes (see App), so focus must
        // work even while loading is still in flight.
        Loaded += (_, _) =>
        {
            Dispatcher.UIThread.Post(() => RepositoryGrid.Focus());
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

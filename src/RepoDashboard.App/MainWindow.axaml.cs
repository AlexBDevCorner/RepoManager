using Avalonia.Controls;
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
    /// <remarks>
    /// RM-005: Do not define a private parameterless
    /// <c>InitializeComponent()</c> here. Avalonia's NameGenerator emits
    /// <c>public void InitializeComponent(bool loadXaml = true)</c> which
    /// loads the XAML and assigns the <c>RepositoryGrid</c> field via the
    /// namescope. A private parameterless overload wins overload
    /// resolution for this call, runs only
    /// <c>AvaloniaXamlLoader.Load(this)</c>, and leaves
    /// <c>RepositoryGrid</c> null, crashing startup in the Loaded handler.
    /// This call intentionally binds to the generated overload.
    /// </remarks>
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
            // RM-005: RepositoryGrid is wired by the generated
            // InitializeComponent via the XAML namescope. Fall back to an
            // explicit lookup so a miswired XAML still resolves when
            // possible. A missing grid is a programming error: fail fast
            // with a clear message instead of a NullReferenceException or
            // silently skipping initial focus.
            var grid = RepositoryGrid
                ?? this.FindControl<DataGrid>("RepositoryGrid")
                ?? throw new InvalidOperationException(
                    "MainWindow.RepositoryGrid could not be resolved after "
                    + "XAML initialization. Ensure MainWindow.axaml defines "
                    + "<DataGrid x:Name=\"RepositoryGrid\" ...> and the "
                    + "generated InitializeComponent wires named controls.");

            Dispatcher.UIThread.Post(() => grid.Focus());
        };
    }
}

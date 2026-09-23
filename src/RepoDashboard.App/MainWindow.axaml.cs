using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    /// <summary>
    /// RM-009: double-clicking a repository row opens that repository's
    /// folder through the existing <c>OpenFolderCommand</c>. Code-behind
    /// only translates the pointer gesture into the existing command: path
    /// validation and launching stay owned by the view model/service.
    /// A double-click outside a row (or without a valid target) does
    /// nothing and never throws; single-click selection is untouched.
    /// </summary>
    private void OnRepositoryGridDoubleTapped(object? sender, TappedEventArgs e) =>
        HandleDoubleTappedSource(e.Source);

    /// <summary>
    /// RM-009: translates a double-tap source into the existing
    /// <c>OpenFolderCommand</c>. Separated from the event signature so the
    /// gesture-to-command translation is unit-testable without synthesizing
    /// pointer events. Never throws for missing/invalid targets.
    /// </summary>
    public void HandleDoubleTappedSource(object? source)
    {
        var viewModel = _viewModel ?? DataContext as MainWindowViewModel;

        if (viewModel is null)
        {
            return;
        }

        var target = TryResolveDoubleClickedRepository(source);

        if (target is null)
        {
            return;
        }

        if (viewModel.OpenFolderCommand.CanExecute(target))
        {
            viewModel.OpenFolderCommand.Execute(target);
        }
    }

    /// <summary>
    /// RM-009: resolves the repository row that produced a pointer gesture
    /// by walking the visual ancestors for a
    /// <see cref="RepositoryRowViewModel"/> data context. Returns
    /// <c>null</c> for empty grid space, headers, or any non-row source so
    /// the caller can no-op instead of throwing.
    /// </summary>
    public static RepositoryRowViewModel? TryResolveDoubleClickedRepository(
        object? source)
    {
        if (source is not Visual visual)
        {
            return null;
        }

        foreach (var ancestor in visual.GetSelfAndVisualAncestors())
        {
            if (ancestor is StyledElement element
                && element.DataContext is RepositoryRowViewModel row)
            {
                return row;
            }
        }

        return null;
    }
}

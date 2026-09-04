using CommunityToolkit.Mvvm.ComponentModel;

namespace RepoDashboard.App.ViewModels;

/// <summary>
/// Main window view model. Always resolved through dependency injection
/// (see <see cref="App"/> composition root); never instantiated manually in views.
/// Application services (for example <c>IRepositoryDashboardService</c> in Task 19)
/// will be added as constructor parameters as they are implemented.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
}

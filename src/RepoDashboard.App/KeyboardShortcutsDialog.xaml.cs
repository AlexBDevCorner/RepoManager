using System.Windows;

namespace RepoDashboard.App;

/// <summary>
/// Read-only keyboard shortcut reference (Task 53). Opened via F1 through
/// ApplicationCommands.Help from MainWindow. No view model: the content is
/// static and no shortcut is editable (customizable keybindings are
/// explicitly out of scope).
/// </summary>
public partial class KeyboardShortcutsDialog : Window
{
    public KeyboardShortcutsDialog()
    {
        InitializeComponent();
    }
}

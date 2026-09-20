using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RepoDashboard.App;

/// <summary>
/// Read-only keyboard shortcut reference. Opened via F1 through
/// <c>ShowHelpCommand</c> on the main view model. No view model: the content
/// is static and no shortcut is editable (customizable keybindings are
/// explicitly out of scope).
/// </summary>
public partial class KeyboardShortcutsDialog : Window
{
    public KeyboardShortcutsDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

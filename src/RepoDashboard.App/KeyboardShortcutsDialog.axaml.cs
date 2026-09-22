using Avalonia.Controls;

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
        // RM-005: binds to the generated InitializeComponent(bool). Do not
        // add a private parameterless overload that would shadow the
        // generated namescope wiring on windows with x:Name controls.
        InitializeComponent();
    }
}

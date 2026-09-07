using Microsoft.Win32;

namespace RepoDashboard.App.Services;

public sealed class FolderPickerService : IFolderPickerService
{
    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };

        return dialog.ShowDialog() == true
            ? dialog.FolderName
            : null;
    }

    public IReadOnlyList<string>? PickFolders(string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = true
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        return dialog.FolderNames;
    }
}

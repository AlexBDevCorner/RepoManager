using System.Diagnostics;

namespace RepoDashboard.App.Services;

internal static class ProcessStartInfoExtensions
{
    internal static ProcessStartInfo WithArguments(this ProcessStartInfo startInfo, string arguments)
    {
        startInfo.Arguments = arguments;
        return startInfo;
    }
}

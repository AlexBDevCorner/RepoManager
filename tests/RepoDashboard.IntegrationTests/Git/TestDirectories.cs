namespace RepoDashboard.IntegrationTests.Git;

/// <summary>
/// Best-effort recursive delete for test working directories.
/// Clears read-only attributes first (Git object files are read-only)
/// and retries transient filesystem locks.
/// </summary>
internal static class TestDirectories
{
    public static void DeleteRecursively(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }

                foreach (var file in Directory.EnumerateFiles(
                    path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                foreach (var directory in Directory.EnumerateDirectories(
                    path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(directory, FileAttributes.Normal);
                }

                File.SetAttributes(path, FileAttributes.Normal);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 2)
            {
                Thread.Sleep(100);
            }
        }
    }
}

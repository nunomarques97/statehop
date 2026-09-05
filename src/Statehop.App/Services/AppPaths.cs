namespace Statehop.App.Services;

/// <summary>
/// Where Statehop keeps its data. Everything is on this machine, under the
/// user's own profile — no cloud, no account, no telemetry.
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// Folder for the activity database. Uses the package's local folder when
    /// running with package identity, which is the MSIX-native location and
    /// gets cleaned up if the package is uninstalled. Falls back to
    /// %LOCALAPPDATA%\Statehop when there is no identity, so the app is still
    /// runnable unpackaged during development.
    /// </summary>
    public static string DataFolder
    {
        get
        {
            try
            {
                return Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            }
            catch (Exception)
            {
                // No package identity (unpackaged launch).
                var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var folder = Path.Combine(root, "Statehop");
                Directory.CreateDirectory(folder);
                return folder;
            }
        }
    }

    /// <summary>Full path of the SQLite activity database.</summary>
    public static string DatabaseFile => Path.Combine(DataFolder, "statehop.db");

    /// <summary>The tray icon, shipped alongside the executable.</summary>
    public static string TrayIconFile =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
}

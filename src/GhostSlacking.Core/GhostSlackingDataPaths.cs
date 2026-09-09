namespace GhostSlacking.Core;

public static class GhostSlackingDataPaths
{
    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GhostSlacking");

    public static string SettingsFilePath => Path.Combine(RootDirectory, "settings.json");

    public static string LogDirectory => Path.Combine(RootDirectory, "logs");
}

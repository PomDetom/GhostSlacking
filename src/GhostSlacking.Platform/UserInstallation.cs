using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace GhostSlacking.Platform;

public static class UserInstallation
{
    public const int ProtocolVersion = 2;

    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static string? Location()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\GhostSlacking");
        var root = key?.GetValue("InstallRoot") as string;
        var location = key?.GetValue("InstallLocation") as string;
        if (key?.GetValue("InstallerKind") as string != "nsis" ||
            key?.GetValue("UpgradeProtocolVersion") is not int protocol || protocol != ProtocolVersion ||
            string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(location)) return null;
        return Path.GetFullPath(Path.Combine(root, "app")).Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)), StringComparison.OrdinalIgnoreCase)
            ? location : null;
    }

    public static bool Matches(string directory) => !IsElevated && Location() is { } location &&
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)).Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), StringComparison.OrdinalIgnoreCase);

    public static bool CloseSafely(string directory)
    {
        var helper = Path.Combine(directory, "GhostSlacking.InstallHelper.exe");
        var info = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("close");
        info.ArgumentList.Add(directory);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Installation helper did not start.");
        // The native helper enforces the 30-second deadline; never kill recovery processes.
        return process.WaitForExit(TimeSpan.FromSeconds(35)) && process.ExitCode == 0;
    }

    public static void ConfirmStarted(string directory)
    {
        if (!Matches(directory)) return;
        var root = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(directory))!;
        if (!File.Exists(Path.Combine(root, "transaction.ini"))) return;
        var info = new ProcessStartInfo(Path.Combine(directory, "GhostSlacking.InstallHelper.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true
        };
        info.ArgumentList.Add("confirm");
        info.ArgumentList.Add(root);
        using var process = Process.Start(info);
        process?.WaitForExit(TimeSpan.FromSeconds(5));
    }
}

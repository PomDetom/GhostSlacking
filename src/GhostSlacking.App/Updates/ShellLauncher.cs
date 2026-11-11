using System.Diagnostics;

namespace GhostSlacking.App;

internal interface IExternalLinkLauncher
{
    bool Open(Uri uri);
}

internal sealed class ShellLauncher : IExternalLinkLauncher
{
    public bool Open(Uri uri) => Start(uri.AbsoluteUri);

    private static bool Start(string target) => Process.Start(new ProcessStartInfo
    {
        FileName = target,
        UseShellExecute = true
    }) is not null;
}

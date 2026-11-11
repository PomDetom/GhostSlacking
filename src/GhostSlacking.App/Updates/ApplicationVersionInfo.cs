using System.Reflection;
using GhostSlacking.Core;

namespace GhostSlacking.App;

internal static partial class ApplicationVersionInfo
{
    public static string Current(Assembly? assembly = null)
    {
        assembly ??= typeof(GhostApplicationController).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var candidate = informational?.Split('+', 2)[0] ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        return ReleaseVersion.TryParse(candidate, out var version) ? version.Text : "0.0.0";
    }
}

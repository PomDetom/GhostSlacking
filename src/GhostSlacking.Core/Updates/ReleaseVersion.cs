using System.Text.RegularExpressions;

namespace GhostSlacking.Core;

public sealed record ReleaseVersion(
    int Major,
    int Minor,
    int Patch,
    string? PreRelease = null) : IComparable<ReleaseVersion>
{
    private static readonly Regex Pattern = new(
        "^(?<major>0|[1-9]\\d*)\\.(?<minor>0|[1-9]\\d*)\\.(?<patch>0|[1-9]\\d*)(?:-(?<pre>(?:alpha|beta|rc)\\.(?:0|[1-9]\\d*)))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public bool IsPreRelease => !string.IsNullOrWhiteSpace(PreRelease);

    public string Text => $"{Major}.{Minor}.{Patch}{(IsPreRelease ? $"-{PreRelease}" : string.Empty)}";

    public string BaseVersionText => $"{Major}.{Minor}.{Patch}";

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        if (!IsPreRelease && !other.IsPreRelease) return 0;
        if (!IsPreRelease) return 1;
        if (!other.IsPreRelease) return -1;

        var left = PreRelease!.Split('.');
        var right = other.PreRelease!.Split('.');
        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            var leftNumeric = int.TryParse(left[index], out var leftNumber);
            var rightNumeric = int.TryParse(right[index], out var rightNumber);
            result = leftNumeric && rightNumeric
                ? leftNumber.CompareTo(rightNumber)
                : leftNumeric != rightNumeric
                    ? (leftNumeric ? -1 : 1)
                    : string.CompareOrdinal(left[index], right[index]);
            if (result != 0) return result;
        }

        return left.Length.CompareTo(right.Length);
    }

    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = default!;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var match = Pattern.Match(text);
        if (!match.Success ||
            !int.TryParse(match.Groups["major"].Value, out var major) ||
            !int.TryParse(match.Groups["minor"].Value, out var minor) ||
            !int.TryParse(match.Groups["patch"].Value, out var patch))
        {
            return false;
        }

        version = new ReleaseVersion(major, minor, patch, match.Groups["pre"].Success
            ? match.Groups["pre"].Value
            : null);
        return true;
    }

    public static ReleaseVersion Parse(string text) =>
        TryParse(text, out var version)
            ? version
            : throw new FormatException($"Invalid release version: {text}");

    public override string ToString() => Text;

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;
    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;
    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;
    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;
}

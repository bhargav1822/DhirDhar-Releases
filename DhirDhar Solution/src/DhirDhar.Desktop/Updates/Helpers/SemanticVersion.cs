using System;
using System.Text.RegularExpressions;

namespace DhirDhar.Desktop.Updates.Helpers;

public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private static readonly Regex VersionRegex = new(
        @"^v?(?<major>\d+)\.(?<minor>\d+)(?:\.(?<patch>\d+))?(?:\.(?<build>\d+))?(?:-(?<prerelease>[0-9A-Za-z\.-]+))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public int Build { get; }
    public string PreRelease { get; }
    public bool IsPreRelease => !string.IsNullOrEmpty(PreRelease);
    public string RawVersion { get; }

    public SemanticVersion(int major, int minor, int patch = 0, int build = 0, string preRelease = "")
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Build = build;
        PreRelease = preRelease ?? string.Empty;
        RawVersion = Build > 0
            ? $"{Major}.{Minor}.{Patch}.{Build}{(IsPreRelease ? $"-{PreRelease}" : "")}"
            : $"{Major}.{Minor}.{Patch}{(IsPreRelease ? $"-{PreRelease}" : "")}";
    }

    public static bool TryParse(string? versionString, out SemanticVersion result)
    {
        result = new SemanticVersion(0, 0, 0);
        if (string.IsNullOrWhiteSpace(versionString)) return false;

        var clean = versionString.Trim();
        var match = VersionRegex.Match(clean);
        if (!match.Success)
        {
            var stripped = Regex.Replace(clean, @"[^0-9.]", "");
            if (Version.TryParse(stripped, out var sysVer))
            {
                result = new SemanticVersion(
                    sysVer.Major < 0 ? 0 : sysVer.Major,
                    sysVer.Minor < 0 ? 0 : sysVer.Minor,
                    sysVer.Build < 0 ? 0 : sysVer.Build,
                    sysVer.Revision < 0 ? 0 : sysVer.Revision);
                return true;
            }
            return false;
        }

        var major = int.Parse(match.Groups["major"].Value);
        var minor = int.Parse(match.Groups["minor"].Value);
        var patch = match.Groups["patch"].Success ? int.Parse(match.Groups["patch"].Value) : 0;
        var build = match.Groups["build"].Success ? int.Parse(match.Groups["build"].Value) : 0;
        var prerelease = match.Groups["prerelease"].Success ? match.Groups["prerelease"].Value : string.Empty;

        result = new SemanticVersion(major, minor, patch, build, prerelease);
        return true;
    }

    public static SemanticVersion Parse(string versionString)
    {
        if (TryParse(versionString, out var result))
            return result;
        throw new FormatException($"Invalid semantic version format: '{versionString}'.");
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;

        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        if (Patch != other.Patch) return Patch.CompareTo(other.Patch);
        if (Build != other.Build) return Build.CompareTo(other.Build);

        if (IsPreRelease && !other.IsPreRelease) return -1;
        if (!IsPreRelease && other.IsPreRelease) return 1;

        return string.Compare(PreRelease, other.PreRelease, StringComparison.OrdinalIgnoreCase);
    }

    public bool Equals(SemanticVersion? other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Build, PreRelease.ToLowerInvariant());

    public override string ToString() => RawVersion;

    public static bool operator ==(SemanticVersion? left, SemanticVersion? right) => Equals(left, right);
    public static bool operator !=(SemanticVersion? left, SemanticVersion? right) => !Equals(left, right);
    public static bool operator <(SemanticVersion? left, SemanticVersion? right) => left is null ? right is not null : left.CompareTo(right) < 0;
    public static bool operator <=(SemanticVersion? left, SemanticVersion? right) => left is null || left.CompareTo(right) <= 0;
    public static bool operator >(SemanticVersion? left, SemanticVersion? right) => left is not null && left.CompareTo(right) > 0;
    public static bool operator >=(SemanticVersion? left, SemanticVersion? right) => left is null ? right is null : left.CompareTo(right) >= 0;
}

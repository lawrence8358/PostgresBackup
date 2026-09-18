using System.Text.RegularExpressions;

using PostgresBackup.Core.Resources;

namespace PostgresBackup.Core.Models;

/// <summary>
/// PostgreSQL 客戶端或伺服器之版本表示模型
/// </summary>
public partial record ToolVersion : IComparable<ToolVersion>
{
    public int Major { get; init; }
    public int Minor { get; init; }
    public int? Patch { get; init; }
    public string RawString { get; init; } = string.Empty;

    public ToolVersion(int major, int minor = 0, int? patch = null, string? rawString = null)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        RawString = rawString ?? (patch.HasValue ? $"{major}.{minor}.{patch}" : $"{major}.{minor}");
    }

    private static readonly Regex VersionPattern = new(
        @"(?:(?:pg_dump|pg_restore|psql|PostgreSQL)\s*(?:\([^)]*\))?\s*)?(\d+)(?:\.(\d+))?(?:\.(\d+))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 嘗試從文字中解析版本號（例如 "pg_dump (PostgreSQL) 16.4" 或 "16.4" 或 "17.0 (Debian)"）
    /// </summary>
    public static bool TryParse(string? raw, out ToolVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var match = VersionPattern.Match(raw);
        if (!match.Success || !match.Groups[1].Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups[1].Value, out var major))
        {
            return false;
        }

        int minor = 0;
        if (match.Groups[2].Success)
        {
            _ = int.TryParse(match.Groups[2].Value, out minor);
        }

        int? patch = null;
        if (match.Groups[3].Success && int.TryParse(match.Groups[3].Value, out var parsedPatch))
        {
            patch = parsedPatch;
        }

        version = new ToolVersion(major, minor, patch, raw.Trim());
        return true;
    }

    public static ToolVersion Parse(string raw)
    {
        if (TryParse(raw, out var version) && version != null)
        {
            return version;
        }

        throw new FormatException(CoreStrings.Format("Error_VersionParseFailed", raw));
    }

    public int CompareTo(ToolVersion? other)
    {
        if (other is null) return 1;

        int majorCompare = Major.CompareTo(other.Major);
        if (majorCompare != 0) return majorCompare;

        int minorCompare = Minor.CompareTo(other.Minor);
        if (minorCompare != 0) return minorCompare;

        int thisPatch = Patch ?? 0;
        int otherPatch = other.Patch ?? 0;
        return thisPatch.CompareTo(otherPatch);
    }

    public override string ToString()
    {
        return Patch.HasValue ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}";
    }

    public static bool operator <(ToolVersion left, ToolVersion right) => left.CompareTo(right) < 0;
    public static bool operator <=(ToolVersion left, ToolVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >(ToolVersion left, ToolVersion right) => left.CompareTo(right) > 0;
    public static bool operator >=(ToolVersion left, ToolVersion right) => left.CompareTo(right) >= 0;
}

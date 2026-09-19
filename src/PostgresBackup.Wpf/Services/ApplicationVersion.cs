using System.Reflection;

namespace PostgresBackup.Wpf.Services;

public static class ApplicationVersion
{
    public static string Current
    {
        get
        {
            var assembly = typeof(ApplicationVersion).Assembly;
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            return Normalize(informationalVersion ?? assembly.GetName().Version?.ToString(3));
        }
    }

    public static string Normalize(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return "0.0.0";
        }

        var normalized = informationalVersion.Trim();
        var metadataSeparator = normalized.IndexOf('+');
        if (metadataSeparator >= 0)
        {
            normalized = normalized[..metadataSeparator];
        }

        return normalized.StartsWith('v') || normalized.StartsWith('V')
            ? normalized[1..]
            : normalized;
    }
}

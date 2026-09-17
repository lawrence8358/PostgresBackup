using System.Globalization;
using System.Resources;

namespace PostgresBackup.Core.Resources;

/// <summary>
/// 提供存取 Core 層多語系字串資源
/// </summary>
public static class CoreStrings
{
    private static readonly ResourceManager _rm =
        new ResourceManager(
            "PostgresBackup.Core.Resources.CoreStrings",
            typeof(CoreStrings).Assembly);

    private static CultureInfo _culture = CultureInfo.InvariantCulture;

    public static void SetCulture(CultureInfo culture) => _culture = culture;

    public static string Get(string key) =>
        _rm.GetString(key, _culture) ?? key;

    public static string Format(string key, params object[] args) =>
        string.Format(Get(key), args);
}

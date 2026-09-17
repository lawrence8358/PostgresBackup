using System.Resources;

namespace PostgresBackup.Wpf.Resources;

/// <summary>
/// 提供存取 WPF 內嵌多語系字串資源
/// </summary>
internal static class Strings
{
    private static ResourceManager? _resourceManager;

    public static ResourceManager ResourceManager =>
        _resourceManager ??= new ResourceManager(
            "PostgresBackup.Wpf.Resources.Strings",
            typeof(Strings).Assembly);
}

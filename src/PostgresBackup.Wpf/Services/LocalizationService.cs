using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using PostgresBackup.Core.Resources;
using PostgresBackup.Wpf.Resources;

namespace PostgresBackup.Wpf.Services;

/// <summary>
/// 提供執行階段語系切換服務
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    public static readonly LocalizationService Instance = new();

    private CultureInfo _currentCulture = CultureInfo.InvariantCulture;

    public record Language(string Code, string DisplayName);

    public static IReadOnlyList<Language> SupportedLanguages { get; } =
    [
        new Language("zh-TW", "繁體中文"),
        new Language("en", "English"),
    ];

    // 英文以 InvariantCulture 表示（中性資源即為英文），其 Name 為空字串，
    // 故須映射回 "en"，否則語系切換器初值會誤選為繁體中文並將介面切回中文。
    public string CurrentLanguageCode =>
        string.IsNullOrEmpty(_currentCulture.Name) ? "en" : _currentCulture.Name;

    public string this[string key] =>
        Strings.ResourceManager.GetString(key, _currentCulture) ?? $"[{key}]";

    /// <summary>取得目前語系之字串資源。</summary>
    public static string S(string key) => Instance[key];

    /// <summary>取得目前語系之字串資源並套用格式化引數。</summary>
    public static string S(string key, params object?[] args) =>
        string.Format(Instance[key], args);

    public void SetLanguage(string cultureCode)
    {
        _currentCulture = cultureCode == "en"
            ? CultureInfo.InvariantCulture
            : new CultureInfo(cultureCode);

        CoreStrings.SetCulture(_currentCulture);

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
    }

    public void InitFromSystem(string? savedCode = null)
    {
        SetLanguage(savedCode ?? DetectSystemLanguage());
    }

    private static string DetectSystemLanguage()
    {
        var uiCulture = CultureInfo.CurrentUICulture;
        foreach (var lang in SupportedLanguages)
        {
            if (lang.Code == "en") continue;
            if (uiCulture.Name.StartsWith(lang.Code, StringComparison.OrdinalIgnoreCase))
                return lang.Code;
        }
        return "zh-TW";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

using PostgresBackup.Wpf.Services;

namespace PostgresBackup.Wpf.Tests;

/// <summary>
/// 釘住「連線設定畫面的適用範圍提示」在英文與繁體中文兩種語系下皆有對應資源字串，
/// 避免未來重構誤刪其中一邊的資源鍵而讓畫面顯示 "[Settings_Connection_ScopeHint]"。
/// </summary>
public class SettingsConnectionScopeHintLocalizationTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("zh-TW")]
    public void 連線設定範圍提示於各語系皆有對應字串(string cultureCode)
    {
        LocalizationService.Instance.SetLanguage(cultureCode);

        var text = LocalizationService.Instance["Settings_Connection_ScopeHint"];

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.NotEqual("[Settings_Connection_ScopeHint]", text);
    }
}

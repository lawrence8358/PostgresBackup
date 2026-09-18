using PostgresBackup.Wpf.Views;

namespace PostgresBackup.Wpf.Tests;

[Collection(WpfAppCollection.Name)]
public class MainWindowNavigationTests(WpfAppFixture fixture)
{
    private readonly WpfAppFixture _fixture = fixture;

    /// <summary>
    /// 迴歸測試：NavSettings 的 Checked 事件於 InitializeComponent 解析側邊欄時即觸發，
    /// 當時 MainContent 尚未建立，導致 SwitchToPage 被 null 防護靜默略過、啟動後內容區域全空白。
    /// </summary>
    [Fact]
    public void 啟動後內容區域應已載入設定頁面()
    {
        Assert.NotNull(_fixture.StartupContent);
        Assert.IsType<SettingsView>(_fixture.StartupContent);
    }

    [Theory]
    [InlineData("Settings", typeof(SettingsView))]
    [InlineData("Backup", typeof(BackupView))]
    [InlineData("Restore", typeof(RestoreView))]
    [InlineData("History", typeof(HistoryView))]
    [InlineData("Log", typeof(LogView))]
    public void 切換導覽頁面應載入對應檢視(string pageName, Type expectedViewType)
    {
        var content = _fixture.OnUi(() =>
        {
            _fixture.Window.SwitchToPage(pageName);
            return WpfAppFixture.ContentOf(_fixture.Window);
        });

        Assert.NotNull(content);
        Assert.IsType(expectedViewType, content);
    }
}

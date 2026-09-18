using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PostgresBackup.Wpf;

namespace PostgresBackup.Wpf.Tests;

/// <summary>
/// 於獨立 STA 執行緒啟動真實 <see cref="App"/>（含完整 DI 容器與 XAML 視覺樹），
/// 供 UI 測試針對實際啟動路徑進行斷言。每個測試處理序僅能存在單一
/// <see cref="Application"/> 實例，故本裝置以 xUnit 集合裝置形式共用。
/// </summary>
public sealed class WpfAppFixture : IDisposable
{
    private readonly Thread _uiThread;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? _startupFailure;

    private App _app = null!;

    /// <summary>主視窗。</summary>
    public MainWindow Window { get; private set; } = null!;

    /// <summary>
    /// 應用程式啟動完成當下、尚未經任何顯式導覽時內容區域所承載的物件。
    /// 於裝置初始化時一次性擷取，使啟動期斷言不受測試執行順序影響。
    /// </summary>
    public object? StartupContent { get; private set; }

    public WpfAppFixture()
    {
        _uiThread = new Thread(UiThreadMain)
        {
            IsBackground = true,
            Name = "WpfAppFixture-UI"
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();

        if (!_ready.Task.Wait(TimeSpan.FromSeconds(60)))
        {
            throw new TimeoutException("WPF 應用程式未能在時限內完成啟動。");
        }

        _ready.Task.GetAwaiter().GetResult();
    }

    private void UiThreadMain()
    {
        try
        {
            var app = new App();
            app.InitializeComponent();

            app.Startup += (_, _) =>
            {
                // 以 ApplicationIdle 排入佇列，確保 OnStartup 與首輪版面配置皆已完成。
                app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    try
                    {
                        _app = app;
                        Window = (MainWindow)app.MainWindow!;
                        StartupContent = ContentOf(Window);
                        _ready.TrySetResult();
                    }
                    catch (Exception ex)
                    {
                        _ready.TrySetException(ex);
                    }
                }));
            };

            app.Run();
        }
        catch (Exception ex)
        {
            _startupFailure = ex;
            _ready.TrySetException(ex);
        }
    }

    /// <summary>取得主視窗內容區域 (MainContent) 目前承載的物件。</summary>
    public static object? ContentOf(MainWindow window)
        => (window.FindName("MainContent") as ContentControl)?.Content;

    /// <summary>於 UI 執行緒上同步執行指定作業並回傳結果。</summary>
    public T OnUi<T>(Func<T> action)
    {
        if (_startupFailure is not null)
        {
            throw new InvalidOperationException("WPF 應用程式啟動失敗。", _startupFailure);
        }

        return _app.Dispatcher.Invoke(action);
    }

    public void Dispose()
    {
        try
        {
            _app?.Dispatcher.Invoke(() => _app.Shutdown());
        }
        catch (Exception)
        {
            // 關閉階段的例外不影響測試結果。
        }

        _uiThread.Join(TimeSpan.FromSeconds(10));
    }
}

[CollectionDefinition(Name)]
public sealed class WpfAppCollection : ICollectionFixture<WpfAppFixture>
{
    public const string Name = "WpfApp";
}

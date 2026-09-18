using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PostgresBackup.Wpf.ViewModels;

namespace PostgresBackup.Wpf.Services;

/// <summary>
/// 提供命令列 --capture 旗標驅動之 WPF 無頭介面截圖與整合測試自動化管線服務。
/// </summary>
public static class UiCaptureService
{
    public static bool IsCaptureMode(string[] args)
    {
        return Array.IndexOf(args, "--capture") >= 0;
    }

    public static async Task RunCaptureAsync(MainWindow mainWindow, IServiceProvider services, string[] args)
    {
        try
        {
            var captureIdx = Array.IndexOf(args, "--capture");
            if (captureIdx < 0 || captureIdx + 1 >= args.Length) return;

            var outputDir = args[captureIdx + 1];
            string? customToolsDir = null;
            var toolsIdx = Array.IndexOf(args, "--custom-tools");
            if (toolsIdx >= 0 && toolsIdx + 1 < args.Length)
            {
                customToolsDir = args[toolsIdx + 1];
            }

            Directory.CreateDirectory(outputDir);
            await Task.Delay(1500);

            if (!string.IsNullOrWhiteSpace(customToolsDir))
            {
                var settingsVm = services.GetRequiredService<SettingsViewModel>();
                settingsVm.CustomPath = customToolsDir;
                await settingsVm.DetectToolsCommand.ExecuteAsync(null);
                await Task.Delay(600);
            }

            var sizes = new (string Name, int Width, int Height)[]
            {
                ("default_960x600", 960, 600),
                ("min_720x480", 720, 480)
            };

            var pages = new[] { "Settings", "Backup", "Restore", "History", "Log" };

            // 啟動當下的原始畫面：刻意不呼叫 SwitchToPage，使本管線涵蓋應用程式
            // 自身的啟動導覽路徑（歷史迴歸：啟動後內容區域空白）。
            mainWindow.UpdateLayout();
            CaptureWindow(mainWindow, Path.Combine(outputDir, "startup_initial.png"));

            foreach (var (sizeName, w, h) in sizes)
            {
                mainWindow.Width = w;
                mainWindow.Height = h;
                mainWindow.UpdateLayout();

                foreach (var page in pages)
                {
                    mainWindow.SwitchToPage(page);
                    await Task.Delay(400);
                    mainWindow.UpdateLayout();

                    CaptureWindow(mainWindow, Path.Combine(outputDir, $"{sizeName}_{page}.png"));
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Capture error: {ex}");
        }
        finally
        {
            Application.Current?.Shutdown(0);
        }
    }

    /// <summary>將指定視窗之目前版面渲染為 PNG 檔案。</summary>
    private static void CaptureWindow(Window window, string filePath)
    {
        int renderW = Math.Max(1, (int)window.ActualWidth);
        int renderH = Math.Max(1, (int)window.ActualHeight);

        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            renderW, renderH, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(window);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));

        using var stream = File.Create(filePath);
        encoder.Save(stream);
    }
}

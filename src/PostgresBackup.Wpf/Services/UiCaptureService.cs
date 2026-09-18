using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PostgresBackup.Core.Models;
using PostgresBackup.Wpf.ViewModels;

namespace PostgresBackup.Wpf.Services;

/// <summary>
/// 提供命令列 --capture 旗標驅動之 WPF 無頭介面截圖與整合測試自動化管線服務。
/// 支援 --custom-tools &lt;dir&gt;、--lang &lt;code&gt; 與 --demo-data（以示範資料取代本機真實資料）。
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

            if (Array.IndexOf(args, "--demo-data") >= 0)
            {
                ApplyDemoData(services);
                await Task.Delay(200);
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

    /// <summary>
    /// 以固定的示範資料取代會洩漏本機環境的欄位（歷史稽核紀錄、預設輸出目錄），
    /// 使產出的文件截圖不含真實資料庫名稱或使用者路徑，且每次截圖結果一致。
    /// </summary>
    private static void ApplyDemoData(IServiceProvider services)
    {
        var day = new DateTimeOffset(2026, 9, 18, 0, 0, 0, DateTimeOffset.Now.Offset);

        services.GetRequiredService<HistoryViewModel>().LoadDemoRecords(
        [
            new BackupRecord
            {
                Timestamp = day.AddHours(2).AddMinutes(15),
                OperationType = BackupOperationType.Backup,
                DatabaseName = "demo_shop",
                FilePath = @"D:\Backups\demo_shop_20260918021500.dump",
                Format = BackupFormat.Custom,
                FileSizeBytes = 250_450_688,
                DurationMs = 132_220,
                Status = BackupStatus.Success
            },
            new BackupRecord
            {
                Timestamp = day.AddHours(9).AddMinutes(31),
                OperationType = BackupOperationType.PreRestoreSnapshot,
                DatabaseName = "demo_shop_staging",
                FilePath = @"D:\Backups\snapshots\demo_shop_staging_20260918093100.dump",
                Format = BackupFormat.Custom,
                FileSizeBytes = 118_374_400,
                DurationMs = 61_480,
                Status = BackupStatus.Success
            },
            new BackupRecord
            {
                Timestamp = day.AddHours(9).AddMinutes(33),
                OperationType = BackupOperationType.Restore,
                DatabaseName = "demo_shop",
                TargetDatabase = "demo_shop_staging",
                FilePath = @"D:\Backups\demo_shop_20260918021500.dump",
                Format = BackupFormat.Custom,
                FileSizeBytes = 250_450_688,
                DurationMs = 86_530,
                Status = BackupStatus.Success
            },
            new BackupRecord
            {
                Timestamp = day.AddHours(14).AddMinutes(8),
                OperationType = BackupOperationType.Backup,
                DatabaseName = "demo_analytics",
                FilePath = @"D:\Backups\demo_analytics_20260918140800.sql",
                Format = BackupFormat.Plain,
                FileSizeBytes = 88_422,
                DurationMs = 1_510,
                Status = BackupStatus.Success
            },
        ]);

        services.GetRequiredService<BackupViewModel>().OutputDirectory = @"D:\Backups";
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

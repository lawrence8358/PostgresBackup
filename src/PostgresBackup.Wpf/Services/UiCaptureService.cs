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

                    int renderW = Math.Max(1, (int)mainWindow.ActualWidth);
                    int renderH = Math.Max(1, (int)mainWindow.ActualHeight);

                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        renderW, renderH, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    rtb.Render(mainWindow);

                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));

                    var filePath = Path.Combine(outputDir, $"{sizeName}_{page}.png");
                    using (var stream = File.Create(filePath))
                    {
                        encoder.Save(stream);
                    }
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
}

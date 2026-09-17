using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Wpf.Services;

namespace PostgresBackup.Wpf.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IToolDetectionService _toolDetector;

    public SettingsViewModel(IToolDetectionService toolDetector)
    {
        _toolDetector = toolDetector;
    }

    // ── 客戶端工具偵測狀態 ──

    [ObservableProperty]
    private ToolDetectionResult _toolResult = ToolDetectionResult.CreateNotFound();

    [ObservableProperty]
    private string? _customPath;

    [ObservableProperty]
    private bool _isDetecting;

    [ObservableProperty]
    private bool _isCopied;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ToolStatus ToolStatus => ToolResult.Status;
    public bool IsReady => ToolResult.IsReady;
    public bool ShowGuide => ToolResult.Status != ToolStatus.Ready;

    public string SourceDisplay => ToolResult.Source switch
    {
        DetectionSource.CustomPath => "使用者指定目錄 (Custom Path)",
        DetectionSource.Path => "系統 PATH 環境變數 (System PATH)",
        DetectionSource.CommonDirectory => "標準安裝目錄 (C:\\Program Files\\PostgreSQL)",
        DetectionSource.Registry => "Windows Registry 登錄檔",
        _ => "無 (None)"
    };

    // ── 資料庫連線設定 ──

    [ObservableProperty]
    private string _host = "localhost";

    [ObservableProperty]
    private int _port = 5432;

    [ObservableProperty]
    private string _database = "postgres";

    [ObservableProperty]
    private string _username = "postgres";

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _isTestingConnection;

    [ObservableProperty]
    private string _connectionStatusMessage = string.Empty;

    [ObservableProperty]
    private bool? _isConnectionSuccessful;

    // ── 命令 ──

    [RelayCommand]
    public async Task DetectToolsAsync()
    {
        if (IsDetecting) return;

        IsDetecting = true;
        try
        {
            ToolResult = await _toolDetector.DetectAsync(CustomPath);

            OnPropertyChanged(nameof(ToolStatus));
            OnPropertyChanged(nameof(IsReady));
            OnPropertyChanged(nameof(ShowGuide));
            OnPropertyChanged(nameof(SourceDisplay));

            StatusMessage = ToolResult.Status switch
            {
                ToolStatus.Ready => $"客戶端工具已就緒（版本: {ToolResult.Version}）",
                ToolStatus.Incompatible => "警告：客戶端工具版本低於伺服器版本！",
                _ => ToolResult.ErrorMessage ?? "未偵測到 PostgreSQL 客戶端工具"
            };
        }
        finally
        {
            IsDetecting = false;
        }
    }

    [RelayCommand]
    private void BrowseCustomPath()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "選取 PostgreSQL 客戶端工具 bin 目錄",
            InitialDirectory = string.IsNullOrWhiteSpace(CustomPath) ? @"C:\Program Files\PostgreSQL" : CustomPath
        };

        if (dialog.ShowDialog() == true)
        {
            CustomPath = dialog.FolderName;
            _ = DetectToolsAsync();
        }
    }

    [RelayCommand]
    private async Task CopyWingetCommandAsync()
    {
        Clipboard.SetText("winget install PostgreSQL.PostgreSQL");
        IsCopied = true;
        await Task.Delay(2000);
        IsCopied = false;
    }

    [RelayCommand]
    private void OpenOfficialUrl()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://www.postgresql.org/download/windows/")
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    private void OpenEdbUrl()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://www.enterprisedb.com/downloads/postgres-postgresql-downloads")
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    public async Task TestConnectionAsync()
    {
        if (IsTestingConnection) return;

        IsTestingConnection = true;
        ConnectionStatusMessage = "正在測試連線與比對版本相容性...";
        IsConnectionSuccessful = null;

        try
        {
            var connSettings = new ConnectionSettings
            {
                Host = Host,
                Port = Port,
                Database = Database,
                Username = Username,
                Password = Password
            };

            var checkResult = await _toolDetector.CheckCompatibilityAsync(
                ToolResult,
                connSettings.ToConnectionString());

            if (checkResult.IsCompatible)
            {
                IsConnectionSuccessful = true;
                ConnectionStatusMessage = $"連線成功！{checkResult.Message}";
            }
            else
            {
                IsConnectionSuccessful = false;
                ConnectionStatusMessage = checkResult.Message;
            }
        }
        catch (Exception ex)
        {
            IsConnectionSuccessful = false;
            ConnectionStatusMessage = $"連線測試失敗: {ex.Message}";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }
}

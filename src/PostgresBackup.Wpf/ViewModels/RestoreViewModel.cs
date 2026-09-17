using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;

namespace PostgresBackup.Wpf.ViewModels;

public partial class RestoreViewModel : ObservableObject
{
    private readonly IRestoreService _restoreService;
    private readonly IConnectionProfileRepository _profileRepo;
    private readonly IToolDetectionService _toolDetector;

    public RestoreViewModel(
        IRestoreService restoreService,
        IConnectionProfileRepository profileRepo,
        IToolDetectionService toolDetector)
    {
        _restoreService = restoreService;
        _profileRepo = profileRepo;
        _toolDetector = toolDetector;
    }

    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];

    [ObservableProperty]
    private ConnectionProfile? _selectedProfile;

    [ObservableProperty]
    private string _sourceFilePath = string.Empty;

    [ObservableProperty]
    private string _targetDatabase = string.Empty;

    [ObservableProperty]
    private RestoreMode _mode = RestoreMode.Normal;

    [ObservableProperty]
    private bool _createPreRestoreSnapshot = true;

    [ObservableProperty]
    private bool _isConfirmed;

    [ObservableProperty]
    private bool _isRestoring;

    [ObservableProperty]
    private string _statusBadgeText = "就緒";

    [ObservableProperty]
    private string _statusMessage = "準備就緒";

    [ObservableProperty]
    private string _terminalOutput = string.Empty;

    private readonly StringBuilder _terminalBuilder = new();

    public async Task InitializeAsync()
    {
        await RefreshProfilesAsync();
    }

    [RelayCommand]
    public async Task RefreshProfilesAsync()
    {
        Profiles.Clear();
        var list = await _profileRepo.GetAllProfilesAsync();
        foreach (var p in list)
        {
            Profiles.Add(p);
        }

        if (Profiles.Count > 0 && SelectedProfile == null)
        {
            SelectedProfile = Profiles[0];
            if (string.IsNullOrWhiteSpace(TargetDatabase))
            {
                TargetDatabase = SelectedProfile.Database;
            }
        }
    }

    partial void OnSelectedProfileChanged(ConnectionProfile? value)
    {
        if (value != null && string.IsNullOrWhiteSpace(TargetDatabase))
        {
            TargetDatabase = value.Database;
        }
    }

    public void SetRestoreTarget(string filePath, string databaseName)
    {
        SourceFilePath = filePath;
        if (!string.IsNullOrWhiteSpace(databaseName))
        {
            TargetDatabase = databaseName;
        }
    }

    [RelayCommand]
    private void BrowseSourceFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "選取備份檔案",
            Filter = "PostgreSQL 備份檔案 (*.dump;*.sql)|*.dump;*.sql|自訂二進位 (*.dump)|*.dump|純文字腳本 (*.sql)|*.sql|所有檔案 (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            SourceFilePath = dialog.FileName;
        }
    }

    [RelayCommand]
    public async Task StartRestoreAsync()
    {
        if (IsRestoring) return;

        if (string.IsNullOrWhiteSpace(SourceFilePath) || !File.Exists(SourceFilePath))
        {
            MessageBox.Show("請先指定有效且存在的備份來源檔案！", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SelectedProfile == null)
        {
            MessageBox.Show("請先選取目標連線設定檔！", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var targetDb = string.IsNullOrWhiteSpace(TargetDatabase) ? SelectedProfile.Database : TargetDatabase;

        if (!IsConfirmed)
        {
            MessageBox.Show("請勾選「高危險操作確認」方塊以確認您已了解資料覆寫風險！", "安全防護提醒", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsRestoring = true;
        StatusBadgeText = "還原中";
        StatusMessage = "正在執行安全還原作業...";
        AppendLog($"[{DateTime.Now:HH:mm:ss}] === 啟動安全還原程序 ===");

        try
        {
            var password = await _profileRepo.GetPasswordAsync(SelectedProfile.Id);
            var connSettings = SelectedProfile.ToConnectionSettings(password);
            connSettings.Database = targetDb;

            var format = RestoreOptions.DetectFormatFromFilePath(SourceFilePath);

            var options = new RestoreOptions
            {
                Connection = connSettings,
                SourceFilePath = SourceFilePath,
                TargetDatabase = targetDb,
                Format = format,
                Mode = Mode,
                CreatePreRestoreSnapshot = CreatePreRestoreSnapshot
            };

            var result = await _restoreService.RestoreAsync(
                options,
                onLogLine: AppendLog);

            if (result.IsSuccess)
            {
                StatusBadgeText = "完成";
                StatusMessage = "資料庫還原順利完成！";
                if (result.SnapshotCreated)
                {
                    StatusMessage += $"（已儲存安全快照: {Path.GetFileName(result.SnapshotFilePath)}）";
                }
            }
            else
            {
                StatusBadgeText = "失敗";
                StatusMessage = $"還原作業中止或失敗：{result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusBadgeText = "錯誤";
            StatusMessage = $"發生未預期錯誤: {ex.Message}";
            AppendLog($"[ERROR] {ex.Message}");
        }
        finally
        {
            IsRestoring = false;
        }
    }

    private void AppendLog(string line)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            _terminalBuilder.AppendLine(line);
            TerminalOutput = _terminalBuilder.ToString();
        });
    }

    [RelayCommand]
    public void ClearLog()
    {
        _terminalBuilder.Clear();
        TerminalOutput = string.Empty;
    }

    [RelayCommand]
    public void CopyLog()
    {
        if (!string.IsNullOrEmpty(TerminalOutput))
        {
            Clipboard.SetText(TerminalOutput);
        }
    }
}

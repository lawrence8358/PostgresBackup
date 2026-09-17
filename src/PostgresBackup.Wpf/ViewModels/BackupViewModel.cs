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

public partial class BackupViewModel : ObservableObject
{
    private readonly IBackupService _backupService;
    private readonly IConnectionProfileRepository _profileRepo;
    private readonly IToolDetectionService _toolDetector;

    public BackupViewModel(
        IBackupService backupService,
        IConnectionProfileRepository profileRepo,
        IToolDetectionService toolDetector)
    {
        _backupService = backupService;
        _profileRepo = profileRepo;
        _toolDetector = toolDetector;

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _outputDirectory = Path.Combine(docs, "PostgresBackups");

        UpdateFileNamePreview();
    }

    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];

    [ObservableProperty]
    private ConnectionProfile? _selectedProfile;

    [ObservableProperty]
    private BackupFormat _format = BackupFormat.Custom;

    [ObservableProperty]
    private BackupMode _mode = BackupMode.SchemaAndData;

    [ObservableProperty]
    private BackupScope _scope = BackupScope.FullDatabase;

    [ObservableProperty]
    private string _schemasText = string.Empty;

    [ObservableProperty]
    private string _tablesText = string.Empty;

    [ObservableProperty]
    private string _outputDirectory;

    [ObservableProperty]
    private string _fileNamePreview = string.Empty;

    [ObservableProperty]
    private bool _isBackingUp;

    [ObservableProperty]
    private string _statusMessage = "準備就緒";

    [ObservableProperty]
    private string _statusBadgeText = "就緒";

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
        }

        UpdateFileNamePreview();
    }

    partial void OnSelectedProfileChanged(ConnectionProfile? value)
    {
        UpdateFileNamePreview();
    }

    partial void OnFormatChanged(BackupFormat value)
    {
        UpdateFileNamePreview();
    }

    private void UpdateFileNamePreview()
    {
        var db = SelectedProfile?.Database ?? "postgres";
        var ext = Format == BackupFormat.Custom ? "dump" : "sql";
        FileNamePreview = $"{db}_{DateTime.Now:yyyyMMddHHmmss}.{ext}";
    }

    [RelayCommand]
    private void BrowseOutputDirectory()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "選取備份檔案存放目錄",
            InitialDirectory = OutputDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            OutputDirectory = dialog.FolderName;
        }
    }

    [RelayCommand]
    public async Task StartBackupAsync()
    {
        if (IsBackingUp) return;

        if (SelectedProfile == null)
        {
            MessageBox.Show("請先選取連線設定檔！若無設定檔，請先至「設定」頁面建立。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBackingUp = true;
        StatusBadgeText = "執行中";
        StatusMessage = "正在執行備份作業...";
        AppendLog($"[{DateTime.Now:HH:mm:ss}] === 開始備份作業 ===");

        try
        {
            var password = await _profileRepo.GetPasswordAsync(SelectedProfile.Id);
            var connSettings = SelectedProfile.ToConnectionSettings(password);

            var schemaList = SchemasText
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            var tableList = TablesText
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            var options = new BackupOptions
            {
                Connection = connSettings,
                Format = Format,
                Mode = Mode,
                Scope = Scope,
                Schemas = schemaList,
                Tables = tableList,
                OutputDirectory = OutputDirectory
            };

            var result = await _backupService.BackupAsync(
                options,
                onLogLine: AppendLog);

            if (result.IsSuccess)
            {
                StatusBadgeText = "完成";
                StatusMessage = $"備份成功！產出檔案：{Path.GetFileName(result.OutputFilePath)}";
            }
            else
            {
                StatusBadgeText = "失敗";
                StatusMessage = $"備份失敗：{result.ErrorMessage}";
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
            IsBackingUp = false;
            UpdateFileNamePreview();
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

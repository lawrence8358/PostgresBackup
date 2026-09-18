using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Wpf.Services;

namespace PostgresBackup.Wpf.ViewModels;

public partial class BackupViewModel : ObservableObject
{
    private readonly IBackupService _backupService;
    private readonly IConnectionProfileRepository _profileRepo;

    public BackupViewModel(
        IBackupService backupService,
        IConnectionProfileRepository profileRepo)
    {
        _backupService = backupService;
        _profileRepo = profileRepo;

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _outputDirectory = Path.Combine(docs, "PostgresBackups");

        UpdateFileNamePreview();
        ResetStatusToIdle();

        // 閒置狀態下的狀態文字須隨語系切換重新在地化；作業進行中或已完成的訊息則保留原文。
        LocalizationService.Instance.PropertyChanged += (_, _) =>
        {
            if (_isStatusIdle) ResetStatusToIdle();
        };
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
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _statusBadgeText = string.Empty;

    private bool _isStatusIdle = true;

    private void ResetStatusToIdle()
    {
        _isStatusIdle = true;
        StatusBadgeText = LocalizationService.S("Status_Idle");
        StatusMessage = LocalizationService.S("Status_Idle_Message");
    }

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
            Title = LocalizationService.S("Backup_Dialog_SelectOutputDir"),
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
            MessageBox.Show(
                LocalizationService.S("Backup_Msg_NoProfile"),
                LocalizationService.S("Common_Notice"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        IsBackingUp = true;
        _isStatusIdle = false;
        StatusBadgeText = LocalizationService.S("Status_Running");
        StatusMessage = LocalizationService.S("Backup_Status_Running");
        AppendLog($"[{DateTime.Now:HH:mm:ss}] {LocalizationService.S("Backup_Log_Start")}");

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
                StatusBadgeText = LocalizationService.S("Status_Completed");
                StatusMessage = LocalizationService.S("Backup_Status_Success", Path.GetFileName(result.OutputFilePath));
            }
            else
            {
                StatusBadgeText = LocalizationService.S("Status_Failed");
                StatusMessage = LocalizationService.S("Backup_Status_Failed", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            StatusBadgeText = LocalizationService.S("Status_Error");
            StatusMessage = LocalizationService.S("Common_UnexpectedError", ex.Message);
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

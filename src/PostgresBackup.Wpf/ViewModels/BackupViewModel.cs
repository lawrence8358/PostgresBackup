using System.Collections.ObjectModel;
using System.IO;
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
    private readonly SettingsViewModel? _settingsViewModel;
    private readonly IClipboardService _clipboardService;
    private readonly BufferedTerminalOutput _terminalOutputBuffer;

    public BackupViewModel(
        IBackupService backupService,
        IConnectionProfileRepository profileRepo,
        SettingsViewModel? settingsViewModel = null,
        IClipboardService? clipboardService = null)
    {
        _backupService = backupService;
        _profileRepo = profileRepo;
        _settingsViewModel = settingsViewModel;
        _clipboardService = clipboardService ?? new WpfClipboardService();
        _terminalOutputBuffer = new BufferedTerminalOutput(output => TerminalOutput = output);

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
    private OperationStatusKind _statusKind = OperationStatusKind.Idle;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _statusBadgeText = string.Empty;

    private bool _isStatusIdle = true;

    private void ResetStatusToIdle()
    {
        _isStatusIdle = true;
        StatusKind = OperationStatusKind.Idle;
        StatusBadgeText = LocalizationService.S("Status_Idle");
        StatusMessage = LocalizationService.S("Status_Idle_Message");
        ErrorDetails = string.Empty;
    }

    [ObservableProperty]
    private string _terminalOutput = string.Empty;

    [ObservableProperty]
    private string _errorDetails = string.Empty;

    public bool HasErrorDetails => !string.IsNullOrWhiteSpace(ErrorDetails);

    partial void OnErrorDetailsChanged(string value) => OnPropertyChanged(nameof(HasErrorDetails));

    public async Task InitializeAsync()
    {
        await RefreshProfilesAsync();
    }

    [RelayCommand]
    public async Task RefreshProfilesAsync()
    {
        Profiles.Clear();

        IReadOnlyList<ConnectionProfile> list;
        try
        {
            list = await _profileRepo.GetAllProfilesAsync();
        }
        catch (Exception ex)
        {
        // 存放區讀不到時不得讓例外逸出：這條路徑由啟動流程觸發，
        // 逸出的例外會變成整個視窗開不起來，而使用者得到的訊息會是
        // 「應用程式當掉了」而不是「這份設定讀不到，原因是……」。
            StatusMessage = LocalizationService.S("Profile_Store_LoadFailed", ex.Message);
            UpdateFileNamePreview();
            return;
        }

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
        StatusKind = OperationStatusKind.Running;
        StatusBadgeText = LocalizationService.S("Status_Running");
        StatusMessage = LocalizationService.S("Backup_Status_Running");
        ErrorDetails = string.Empty;
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
                OutputDirectory = OutputDirectory,
                ClientToolDirectory = _settingsViewModel?.CustomPath
            };

            var result = await _backupService.BackupAsync(
                options,
                onLogLine: AppendLog);
            await _terminalOutputBuffer.FlushAsync();

            if (result.IsSuccess)
            {
                StatusKind = OperationStatusKind.Completed;
                StatusBadgeText = LocalizationService.S("Status_Completed");
                StatusMessage = LocalizationService.S("Backup_Status_Success", Path.GetFileName(result.OutputFilePath));
                ErrorDetails = string.Empty;
            }
            else
            {
                StatusKind = OperationStatusKind.Failed;
                StatusBadgeText = LocalizationService.S("Status_Failed");
                StatusMessage = LocalizationService.S("Backup_Status_Failed_Summary");
                ErrorDetails = result.ErrorMessage ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
            await _terminalOutputBuffer.FlushAsync();
            StatusKind = OperationStatusKind.Error;
            StatusBadgeText = LocalizationService.S("Status_Error");
            StatusMessage = LocalizationService.S("Common_UnexpectedError_Summary");
            ErrorDetails = ex.Message;
        }
        finally
        {
            IsBackingUp = false;
            UpdateFileNamePreview();
        }
    }

    private void AppendLog(string line)
    {
        _terminalOutputBuffer.Append(line);
    }

    [RelayCommand]
    public void ClearLog()
    {
        _terminalOutputBuffer.Clear();
    }

    [RelayCommand]
    public void CopyLog()
    {
        var output = _terminalOutputBuffer.Content;
        if (!string.IsNullOrEmpty(output))
        {
            _clipboardService.SetText(output);
        }
    }

    [RelayCommand]
    public void CopyErrorDetails()
    {
        if (HasErrorDetails)
        {
            _clipboardService.SetText(ErrorDetails);
        }
    }
}

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

public partial class RestoreViewModel : ObservableObject
{
    private readonly IRestoreService _restoreService;
    private readonly IConnectionProfileRepository _profileRepo;
    private readonly SettingsViewModel? _settingsViewModel;
    private readonly IClipboardService _clipboardService;
    private readonly BufferedTerminalOutput _terminalOutputBuffer;

    public RestoreViewModel(
        IRestoreService restoreService,
        IConnectionProfileRepository profileRepo,
        SettingsViewModel? settingsViewModel = null,
        IClipboardService? clipboardService = null)
    {
        _restoreService = restoreService;
        _profileRepo = profileRepo;
        _settingsViewModel = settingsViewModel;
        _clipboardService = clipboardService ?? new WpfClipboardService();
        _terminalOutputBuffer = new BufferedTerminalOutput(output => TerminalOutput = output);

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
    private OperationStatusKind _statusKind = OperationStatusKind.Idle;

    [ObservableProperty]
    private string _statusBadgeText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    private bool _isStatusIdle = true;

    public bool CanStartRestore => IsConfirmed && !IsRestoring;

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
            return;
        }

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

    partial void OnIsConfirmedChanged(bool value) => OnPropertyChanged(nameof(CanStartRestore));

    partial void OnIsRestoringChanged(bool value) => OnPropertyChanged(nameof(CanStartRestore));

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
            Title = LocalizationService.S("Restore_Dialog_SelectSourceFile"),
            Filter = LocalizationService.S("Restore_Dialog_FileFilter")
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
            MessageBox.Show(
                LocalizationService.S("Restore_Msg_NoSourceFile"),
                LocalizationService.S("Common_Warning"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (SelectedProfile == null)
        {
            MessageBox.Show(
                LocalizationService.S("Restore_Msg_NoProfile"),
                LocalizationService.S("Common_Warning"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var targetDb = string.IsNullOrWhiteSpace(TargetDatabase) ? SelectedProfile.Database : TargetDatabase;

        if (!IsConfirmed)
        {
            MessageBox.Show(
                LocalizationService.S("Restore_Msg_NotConfirmed"),
                LocalizationService.S("Restore_Msg_SafetyGuardTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        IsRestoring = true;
        _isStatusIdle = false;
        StatusKind = OperationStatusKind.Running;
        StatusBadgeText = LocalizationService.S("Status_Restoring");
        StatusMessage = LocalizationService.S("Restore_Status_Running");
        ErrorDetails = string.Empty;
        AppendLog($"[{DateTime.Now:HH:mm:ss}] {LocalizationService.S("Restore_Log_Start")}");

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
                CreatePreRestoreSnapshot = CreatePreRestoreSnapshot,
                ClientToolDirectory = _settingsViewModel?.CustomPath
            };

            var result = await _restoreService.RestoreAsync(
                options,
                onLogLine: AppendLog);
            await _terminalOutputBuffer.FlushAsync();

            if (result.IsSuccess)
            {
                StatusKind = OperationStatusKind.Completed;
                StatusBadgeText = LocalizationService.S("Status_Completed");
                StatusMessage = LocalizationService.S("Restore_Status_Success");
                if (result.SnapshotCreated)
                {
                    StatusMessage += LocalizationService.S(
                        "Restore_Status_SnapshotSaved", Path.GetFileName(result.SnapshotFilePath));
                }

                ErrorDetails = string.Empty;
            }
            else
            {
                StatusKind = OperationStatusKind.Failed;
                StatusBadgeText = LocalizationService.S("Status_Failed");
                StatusMessage = LocalizationService.S("Restore_Status_Failed_Summary");
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
            IsRestoring = false;
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

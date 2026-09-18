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

public partial class RestoreViewModel : ObservableObject
{
    private readonly IRestoreService _restoreService;
    private readonly IConnectionProfileRepository _profileRepo;

    public RestoreViewModel(
        IRestoreService restoreService,
        IConnectionProfileRepository profileRepo)
    {
        _restoreService = restoreService;
        _profileRepo = profileRepo;

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
    private string _statusBadgeText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

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
        StatusBadgeText = LocalizationService.S("Status_Restoring");
        StatusMessage = LocalizationService.S("Restore_Status_Running");
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
                CreatePreRestoreSnapshot = CreatePreRestoreSnapshot
            };

            var result = await _restoreService.RestoreAsync(
                options,
                onLogLine: AppendLog);

            if (result.IsSuccess)
            {
                StatusBadgeText = LocalizationService.S("Status_Completed");
                StatusMessage = LocalizationService.S("Restore_Status_Success");
                if (result.SnapshotCreated)
                {
                    StatusMessage += LocalizationService.S(
                        "Restore_Status_SnapshotSaved", Path.GetFileName(result.SnapshotFilePath));
                }
            }
            else
            {
                StatusBadgeText = LocalizationService.S("Status_Failed");
                StatusMessage = LocalizationService.S("Restore_Status_Failed", result.ErrorMessage);
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

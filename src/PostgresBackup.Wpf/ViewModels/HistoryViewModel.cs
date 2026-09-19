using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Wpf.Services;

namespace PostgresBackup.Wpf.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly IBackupHistoryRepository _historyRepo;

    public event Action<BackupRecord>? RequestRestore;

    public HistoryViewModel(IBackupHistoryRepository historyRepo)
    {
        _historyRepo = historyRepo;
    }

    public ObservableCollection<BackupRecord> Records { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _selectedFilterIndex; // 0: All, 1: Backup, 2: Restore, 3: Snapshot

    [ObservableProperty]
    private BackupRecord? _selectedRecord;

    [ObservableProperty]
    private bool _isLoading;

    private bool _isDemoMode;

    /// <summary>
    /// 以指定的示範紀錄取代歷史清單，並停用資料庫載入。
    /// 供 --capture 自動化截圖使用，避免將本機真實稽核資料帶入文件截圖。
    /// </summary>
    public void LoadDemoRecords(IEnumerable<BackupRecord> records)
    {
        _isDemoMode = true;
        Records.Clear();
        foreach (var record in records)
        {
            Records.Add(record);
        }

        SelectedRecord = Records.Count > 1 ? Records[1] : Records.FirstOrDefault();
    }

    [RelayCommand]
    public async Task LoadRecordsAsync()
    {
        if (IsLoading || _isDemoMode) return;

        IsLoading = true;
        try
        {
            var filter = new BackupHistoryFilter
            {
                DatabaseName = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
                Limit = 200
            };

            filter.OperationType = SelectedFilterIndex switch
            {
                1 => BackupOperationType.Backup,
                2 => BackupOperationType.Restore,
                3 => BackupOperationType.PreRestoreSnapshot,
                _ => null
            };

            var list = await _historyRepo.GetRecordsAsync(filter);

            Records.Clear();
            foreach (var item in list)
            {
                Records.Add(item);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = LoadRecordsAsync();
    }

    partial void OnSelectedFilterIndexChanged(int value)
    {
        _ = LoadRecordsAsync();
    }

    [RelayCommand]
    public void OpenFileLocation(BackupRecord? record)
    {
        var target = record ?? SelectedRecord;
        if (target == null || string.IsNullOrWhiteSpace(target.FilePath)) return;

        try
        {
            if (File.Exists(target.FilePath))
            {
                Process.Start("explorer.exe", $"/select,\"{target.FilePath}\"");
            }
            else
            {
                var dir = Path.GetDirectoryName(target.FilePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    Process.Start("explorer.exe", $"\"{dir}\"");
                }
                else
                {
                    MessageBox.Show(
                        LocalizationService.S("History_Msg_FileMissing", target.FilePath),
                        LocalizationService.S("Common_Notice"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                LocalizationService.S("History_Msg_ExplorerFailed", ex.Message),
                LocalizationService.S("Common_Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void InitiateRestore(BackupRecord? record)
    {
        var target = record ?? SelectedRecord;
        if (target == null) return;

        RequestRestore?.Invoke(target);
    }

    [RelayCommand]
    public async Task DeleteRecordAsync(BackupRecord? record)
    {
        var target = record ?? SelectedRecord;
        if (target == null) return;

        var confirm = MessageBox.Show(
            LocalizationService.S("History_Msg_ConfirmDelete", target.DatabaseName, target.FilePath),
            LocalizationService.S("History_Msg_ConfirmDeleteTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            await _historyRepo.DeleteRecordAsync(target.Id);
            Records.Remove(target);
        }
    }
}

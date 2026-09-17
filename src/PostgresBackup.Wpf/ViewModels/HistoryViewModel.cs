using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;

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

    [RelayCommand]
    public async Task LoadRecordsAsync()
    {
        if (IsLoading) return;

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
                    MessageBox.Show($"檔案或資料夾已不存在：\n{target.FilePath}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"無法開啟檔案總管: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
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
            $"確定要自歷史紀錄中刪除此項紀錄嗎？（實體檔案將被保留）\n資料庫: {target.DatabaseName}\n檔案: {target.FilePath}",
            "確認刪除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            await _historyRepo.DeleteRecordAsync(target.Id);
            Records.Remove(target);
        }
    }
}

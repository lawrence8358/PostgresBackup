using System.Collections.ObjectModel;
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
    private readonly IConnectionProfileRepository _profileRepo;

    public SettingsViewModel(
        IToolDetectionService toolDetector,
        IConnectionProfileRepository profileRepo)
    {
        _toolDetector = toolDetector;
        _profileRepo = profileRepo;
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

    // ── 資料庫連線設定與設定檔管理 ──

    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];

    [ObservableProperty]
    private ConnectionProfile? _selectedProfile;

    [ObservableProperty]
    private string _profileName = "本機預設連線";

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

    [ObservableProperty]
    private string _serverVersionDisplay = string.Empty;

    // ── 生命週期與初始化 ──

    public async Task InitializeAsync()
    {
        await LoadProfilesAsync();
        await DetectToolsAsync();
    }

    [RelayCommand]
    public async Task LoadProfilesAsync()
    {
        Profiles.Clear();
        var list = await _profileRepo.GetAllProfilesAsync();
        foreach (var p in list)
        {
            Profiles.Add(p);
        }

        if (Profiles.Count > 0)
        {
            SelectedProfile = Profiles[0];
        }
        else
        {
            NewProfile();
        }
    }

    partial void OnSelectedProfileChanged(ConnectionProfile? value)
    {
        if (value != null)
        {
            ProfileName = value.Name;
            Host = value.Host;
            Port = value.Port;
            Database = value.Database;
            Username = value.Username;

            _ = LoadProfilePasswordAsync(value.Id);
        }
    }

    private async Task LoadProfilePasswordAsync(string profileId)
    {
        Password = await _profileRepo.GetPasswordAsync(profileId) ?? string.Empty;
    }

    [RelayCommand]
    public void NewProfile()
    {
        SelectedProfile = null;
        ProfileName = "新連線設定檔";
        Host = "localhost";
        Port = 5432;
        Database = "postgres";
        Username = "postgres";
        Password = string.Empty;
        ConnectionStatusMessage = string.Empty;
        IsConnectionSuccessful = null;
        ServerVersionDisplay = string.Empty;
    }

    [RelayCommand]
    public async Task SaveProfileAsync()
    {
        var profile = SelectedProfile ?? new ConnectionProfile();
        profile.Name = string.IsNullOrWhiteSpace(ProfileName) ? "未命名連線" : ProfileName;
        profile.Host = Host;
        profile.Port = Port;
        profile.Database = Database;
        profile.Username = Username;
        profile.LastUsedAt = DateTimeOffset.UtcNow;

        await _profileRepo.SaveProfileAsync(profile, Password);

        await LoadProfilesAsync();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);

        ConnectionStatusMessage = "連線設定檔已安全儲存（密碼已存放於 Windows 憑證庫）！";
        IsConnectionSuccessful = true;
    }

    [RelayCommand]
    public async Task DeleteProfileAsync()
    {
        if (SelectedProfile == null) return;

        var id = SelectedProfile.Id;
        await _profileRepo.DeleteProfileAsync(id);
        await LoadProfilesAsync();
    }

    // ── 客戶端工具命令 ──

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
        ServerVersionDisplay = string.Empty;

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
                if (checkResult.ServerMajorVersion != null)
                {
                    ServerVersionDisplay = $"伺服器版本: PostgreSQL {checkResult.ServerMajorVersion} ({checkResult.ServerVersionString})";
                }
            }
            else
            {
                IsConnectionSuccessful = false;
                ConnectionStatusMessage = checkResult.Message;
                if (checkResult.ServerMajorVersion != null)
                {
                    ServerVersionDisplay = $"伺服器版本: PostgreSQL {checkResult.ServerMajorVersion}（需至少客戶端工具同版本）";
                }
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

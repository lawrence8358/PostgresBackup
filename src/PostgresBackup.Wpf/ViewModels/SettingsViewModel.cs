using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;
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

        LocalizationService.Instance.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CopyButtonText));
            OnPropertyChanged(nameof(SourceDisplay));
            OnPropertyChanged(nameof(ToolVersionDisplay));
            RefreshToolStatusMessage();
        };
    }

    // ── 客戶端工具偵測狀態 ──

    [ObservableProperty]
    private ToolDetectionResult _toolResult = ToolDetectionResult.CreateNotFound();

    [ObservableProperty]
    private string? _customPath;

    [ObservableProperty]
    private bool _isDetecting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CopyButtonText))]
    private bool _isCopied;

    public string CopyButtonText => IsCopied
        ? $"✓ {LocalizationService.Instance["Settings_Guide_Copied"]}"
        : $"📋 {LocalizationService.Instance["Settings_Guide_CopyCommand"]}";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ToolStatus ToolStatus => ToolResult.Status;
    public bool IsReady => ToolResult.IsReady;
    public bool ShowGuide => ToolResult.Status != ToolStatus.Ready;

    public string SourceDisplay => LocalizationService.S("Settings_ToolSource_Format", ToolResult.Source switch
    {
        DetectionSource.CustomPath => LocalizationService.S("Settings_Source_CustomPath"),
        DetectionSource.Path => LocalizationService.S("Settings_Source_Path"),
        DetectionSource.CommonDirectory => LocalizationService.S("Settings_Source_CommonDirectory"),
        DetectionSource.Registry => LocalizationService.S("Settings_Source_Registry"),
        _ => LocalizationService.S("Settings_Source_None")
    });

    public string ToolVersionDisplay => LocalizationService.S("Settings_ToolVersion_Format", ToolResult.Version);

    // ── 資料庫連線設定與設定檔管理 ──

    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];

    [ObservableProperty]
    private ConnectionProfile? _selectedProfile;

    [ObservableProperty]
    private string _profileName = LocalizationService.S("Settings_Profile_DefaultName");

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
            // NewProfile 會清空狀態訊息，因此必須先讓表單回到乾淨狀態，再說明原因。
            NewProfile();
            ConnectionStatusMessage = LocalizationService.S("Profile_Store_LoadFailed", ex.Message);
            IsConnectionSuccessful = false;
            return;
        }

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
        // 以 _ = 的形式被呼叫，因此這裡逸出的例外不會有人觀察到，
        // 使用者只會看到密碼欄莫名其妙是空的。讀不到就說出來。
        try
        {
            Password = await _profileRepo.GetPasswordAsync(profileId) ?? string.Empty;
        }
        catch (Exception ex)
        {
            Password = string.Empty;
            ConnectionStatusMessage = LocalizationService.S("Profile_Store_PasswordLoadFailed", ex.Message);
            IsConnectionSuccessful = false;
        }
    }

    [RelayCommand]
    public void NewProfile()
    {
        SelectedProfile = null;
        ProfileName = LocalizationService.S("Settings_Profile_NewName");
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
        profile.Name = string.IsNullOrWhiteSpace(ProfileName)
            ? LocalizationService.S("Settings_Profile_UnnamedName")
            : ProfileName;
        profile.Host = Host;
        profile.Port = Port;
        profile.Database = Database;
        profile.Username = Username;
        profile.LastUsedAt = DateTimeOffset.UtcNow;

        try
        {
            await _profileRepo.SaveProfileAsync(profile, Password);
        }
        catch (Exception ex)
        {
            // 密碼無法安全儲存時必須立即明示失敗，不得讓使用者誤以為已儲存成功。
            ConnectionStatusMessage = LocalizationService.S("Settings_Profile_SaveFailedMessage", ex.Message);
            IsConnectionSuccessful = false;
            return;
        }

        await LoadProfilesAsync();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);

        ConnectionStatusMessage = LocalizationService.S("Settings_Profile_SavedMessage");
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
            OnPropertyChanged(nameof(ToolVersionDisplay));

            RefreshToolStatusMessage();
        }
        finally
        {
            IsDetecting = false;
        }
    }

    private void RefreshToolStatusMessage()
    {
        StatusMessage = ToolResult.Status switch
        {
            ToolStatus.Ready => LocalizationService.S("Settings_ToolStatus_ReadyMessage", ToolResult.Version),
            ToolStatus.Incompatible => LocalizationService.S("Settings_ToolStatus_IncompatibleMessage"),
            _ => ToolResult.ErrorMessage ?? LocalizationService.S("Settings_ToolStatus_NotFoundMessage")
        };
    }

    [RelayCommand]
    private void BrowseCustomPath()
    {
        var dialog = new OpenFolderDialog
        {
            Title = LocalizationService.S("Settings_Dialog_SelectBinDir"),
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
        ConnectionStatusMessage = LocalizationService.S("Settings_Connection_Testing");
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
                ConnectionStatusMessage = LocalizationService.S("Settings_Connection_SuccessMessage", checkResult.Message);
                if (checkResult.ServerMajorVersion != null)
                {
                    ServerVersionDisplay = LocalizationService.S(
                        "Settings_ServerVersion_Format", checkResult.ServerMajorVersion, checkResult.ServerVersionString);
                }
            }
            else
            {
                IsConnectionSuccessful = false;
                // 失敗訊息可能來自資料庫驅動程式，連線字串格式異常時有可能夾帶其內容。
                ConnectionStatusMessage = SensitiveText.Redact(checkResult.Message, connSettings.Password);
                if (checkResult.ServerMajorVersion != null)
                {
                    ServerVersionDisplay = LocalizationService.S(
                        "Settings_ServerVersion_Incompatible", checkResult.ServerMajorVersion);
                }
            }
        }
        catch (Exception ex)
        {
            IsConnectionSuccessful = false;
            ConnectionStatusMessage = LocalizationService.S(
                "Settings_Connection_TestFailed", SensitiveText.Redact(ex.Message, Password));
        }
        finally
        {
            IsTestingConnection = false;
        }
    }
}

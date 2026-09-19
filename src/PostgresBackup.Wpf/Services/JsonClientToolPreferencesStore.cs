using System.IO;
using System.Text.Json;

namespace PostgresBackup.Wpf.Services;

/// <summary>
/// Stores non-secret client-tool preferences for the current Windows user.
/// </summary>
public sealed class JsonClientToolPreferencesStore : IClientToolPreferencesStore
{
    private readonly string _settingsPath;

    public JsonClientToolPreferencesStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PostgresBackup",
            "settings.json"))
    {
    }

    public JsonClientToolPreferencesStore(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public async Task<string?> LoadCustomToolDirectoryAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_settingsPath);
        var preferences = await JsonSerializer.DeserializeAsync<ClientToolPreferences>(stream, cancellationToken: cancellationToken);
        return preferences?.CustomToolDirectory;
    }

    public async Task SaveCustomToolDirectoryAsync(string customToolDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customToolDirectory);

        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("The preferences path must include a directory.");
        Directory.CreateDirectory(directory);

        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(
            stream,
            new ClientToolPreferences { CustomToolDirectory = customToolDirectory },
            cancellationToken: cancellationToken);
    }

    private sealed class ClientToolPreferences
    {
        public string? CustomToolDirectory { get; init; }
    }
}

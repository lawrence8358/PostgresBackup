namespace PostgresBackup.Wpf.Services;

public interface IClientToolPreferencesStore
{
    Task<string?> LoadCustomToolDirectoryAsync(CancellationToken cancellationToken = default);

    Task SaveCustomToolDirectoryAsync(string customToolDirectory, CancellationToken cancellationToken = default);
}

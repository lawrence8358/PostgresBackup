namespace PostgresBackup.Wpf.Services;

internal sealed class NullClientToolPreferencesStore : IClientToolPreferencesStore
{
    public Task<string?> LoadCustomToolDirectoryAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task SaveCustomToolDirectoryAsync(string customToolDirectory, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

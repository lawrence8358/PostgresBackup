using Microsoft.Data.Sqlite;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Services;

/// <summary>
/// 基於 SQLite 實作之備份與還原歷史稽核倉儲
/// </summary>
public class SqliteBackupHistoryRepository : IBackupHistoryRepository, IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _initialized;
    private SqliteConnection? _inMemoryConnection;

    public SqliteBackupHistoryRepository(string? dbPath = null)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(localApp, "PostgresBackup");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var path = Path.Combine(dir, "history.db");
            _connectionString = $"Data Source={path}";
        }
        else if (dbPath.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase) || dbPath == ":memory:")
        {
            _connectionString = dbPath == ":memory:" ? "Data Source=:memory:" : dbPath;
        }
        else
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _connectionString = $"Data Source={dbPath}";
        }
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await _lock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            if (_connectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
            {
                _inMemoryConnection = new SqliteConnection(_connectionString);
                await _inMemoryConnection.OpenAsync(ct);
            }

            await using var lease = await GetConnectionLeaseAsync(ct);
            const string sql = """
                CREATE TABLE IF NOT EXISTS backup_history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp TEXT NOT NULL,
                    operation_type TEXT NOT NULL,
                    database_name TEXT NOT NULL,
                    target_database TEXT,
                    file_path TEXT NOT NULL,
                    format TEXT NOT NULL,
                    file_size_bytes INTEGER NOT NULL,
                    duration_ms INTEGER NOT NULL,
                    status TEXT NOT NULL,
                    error_message TEXT,
                    arguments TEXT,
                    created_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_history_timestamp ON backup_history(timestamp DESC);
                CREATE INDEX IF NOT EXISTS idx_history_dbname ON backup_history(database_name);
                """;

            await using var cmd = new SqliteCommand(sql, lease.Connection);
            await cmd.ExecuteNonQueryAsync(ct);

            _initialized = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<BackupRecord> AddRecordAsync(BackupRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await InitializeAsync(ct);

        await using var lease = await GetConnectionLeaseAsync(ct);
        const string sql = """
            INSERT INTO backup_history (
                timestamp, operation_type, database_name, target_database,
                file_path, format, file_size_bytes, duration_ms,
                status, error_message, arguments, created_at
            ) VALUES (
                @timestamp, @operation_type, @database_name, @target_database,
                @file_path, @format, @file_size_bytes, @duration_ms,
                @status, @error_message, @arguments, @created_at
            );
            SELECT last_insert_rowid();
            """;

        await using var cmd = new SqliteCommand(sql, lease.Connection);
        cmd.Parameters.AddWithValue("@timestamp", record.Timestamp.ToString("O"));
        cmd.Parameters.AddWithValue("@operation_type", record.OperationType.ToString());
        cmd.Parameters.AddWithValue("@database_name", record.DatabaseName);
        cmd.Parameters.AddWithValue("@target_database", (object?)record.TargetDatabase ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@file_path", record.FilePath);
        cmd.Parameters.AddWithValue("@format", record.Format.ToString());
        cmd.Parameters.AddWithValue("@file_size_bytes", record.FileSizeBytes);
        cmd.Parameters.AddWithValue("@duration_ms", record.DurationMs);
        cmd.Parameters.AddWithValue("@status", record.Status.ToString());
        cmd.Parameters.AddWithValue("@error_message", (object?)record.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@arguments", record.Arguments);
        cmd.Parameters.AddWithValue("@created_at", record.CreatedAt.ToString("O"));

        var newId = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
        record.Id = newId;
        return record;
    }

    public async Task<IReadOnlyList<BackupRecord>> GetRecordsAsync(BackupHistoryFilter? filter = null, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        await using var lease = await GetConnectionLeaseAsync(ct);
        var whereClauses = new List<string>();
        var parameters = new List<SqliteParameter>();

        if (filter != null)
        {
            if (filter.OperationType.HasValue)
            {
                whereClauses.Add("operation_type = @opType");
                parameters.Add(new SqliteParameter("@opType", filter.OperationType.Value.ToString()));
            }

            if (filter.Status.HasValue)
            {
                whereClauses.Add("status = @status");
                parameters.Add(new SqliteParameter("@status", filter.Status.Value.ToString()));
            }

            if (!string.IsNullOrWhiteSpace(filter.DatabaseName))
            {
                whereClauses.Add("(database_name LIKE @db OR target_database LIKE @db)");
                parameters.Add(new SqliteParameter("@db", $"%{filter.DatabaseName.Trim()}%"));
            }

            if (filter.FromDate.HasValue)
            {
                whereClauses.Add("timestamp >= @fromDate");
                parameters.Add(new SqliteParameter("@fromDate", filter.FromDate.Value.ToString("O")));
            }

            if (filter.ToDate.HasValue)
            {
                whereClauses.Add("timestamp <= @toDate");
                parameters.Add(new SqliteParameter("@toDate", filter.ToDate.Value.ToString("O")));
            }
        }

        var whereSql = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : string.Empty;
        var limit = filter?.Limit > 0 ? filter.Limit : 100;

        var sql = $"""
            SELECT id, timestamp, operation_type, database_name, target_database,
                   file_path, format, file_size_bytes, duration_ms,
                   status, error_message, arguments, created_at
            FROM backup_history
            {whereSql}
            ORDER BY timestamp DESC
            LIMIT {limit}
            """;

        await using var cmd = new SqliteCommand(sql, lease.Connection);
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(p);
        }

        var results = new List<BackupRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapRecord(reader));
        }

        return results;
    }

    public async Task<BackupRecord?> GetRecordByIdAsync(long id, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        await using var lease = await GetConnectionLeaseAsync(ct);
        const string sql = """
            SELECT id, timestamp, operation_type, database_name, target_database,
                   file_path, format, file_size_bytes, duration_ms,
                   status, error_message, arguments, created_at
            FROM backup_history
            WHERE id = @id
            """;

        await using var cmd = new SqliteCommand(sql, lease.Connection);
        cmd.Parameters.AddWithValue("@id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapRecord(reader);
        }

        return null;
    }

    public async Task<bool> DeleteRecordAsync(long id, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        await using var lease = await GetConnectionLeaseAsync(ct);
        const string sql = "DELETE FROM backup_history WHERE id = @id";

        await using var cmd = new SqliteCommand(sql, lease.Connection);
        cmd.Parameters.AddWithValue("@id", id);

        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    private static BackupRecord MapRecord(SqliteDataReader reader)
    {
        Enum.TryParse<BackupOperationType>(reader.GetString(2), out var opType);
        Enum.TryParse<BackupFormat>(reader.GetString(6), out var format);
        Enum.TryParse<BackupStatus>(reader.GetString(9), out var status);

        return new BackupRecord
        {
            Id = reader.GetInt64(0),
            Timestamp = DateTimeOffset.Parse(reader.GetString(1)),
            OperationType = opType,
            DatabaseName = reader.GetString(3),
            TargetDatabase = reader.IsDBNull(4) ? null : reader.GetString(4),
            FilePath = reader.GetString(5),
            Format = format,
            FileSizeBytes = reader.GetInt64(7),
            DurationMs = reader.GetInt64(8),
            Status = status,
            ErrorMessage = reader.IsDBNull(10) ? null : reader.GetString(10),
            Arguments = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(12))
        };
    }

    private async Task<ConnectionLease> GetConnectionLeaseAsync(CancellationToken ct)
    {
        if (_inMemoryConnection != null)
        {
            return new ConnectionLease(_inMemoryConnection, shouldDispose: false);
        }

        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        return new ConnectionLease(conn, shouldDispose: true);
    }

    private sealed class ConnectionLease : IAsyncDisposable
    {
        public SqliteConnection Connection { get; }
        private readonly bool _shouldDispose;

        public ConnectionLease(SqliteConnection connection, bool shouldDispose)
        {
            Connection = connection;
            _shouldDispose = shouldDispose;
        }

        public async ValueTask DisposeAsync()
        {
            if (_shouldDispose)
            {
                await Connection.DisposeAsync();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_inMemoryConnection != null)
        {
            await _inMemoryConnection.DisposeAsync();
            _inMemoryConnection = null;
        }
        _lock.Dispose();
    }
}

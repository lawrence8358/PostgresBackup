using PostgresBackup.Core.Models;

namespace PostgresBackup.Wpf.Extensions;

public static class BackupFormatHelper
{
    public static BackupFormat Custom => BackupFormat.Custom;
    public static BackupFormat Plain => BackupFormat.Plain;
}

public static class BackupModeHelper
{
    public static BackupMode SchemaAndData => BackupMode.SchemaAndData;
    public static BackupMode SchemaOnly => BackupMode.SchemaOnly;
    public static BackupMode DataOnly => BackupMode.DataOnly;
}

public static class BackupScopeHelper
{
    public static BackupScope FullDatabase => BackupScope.FullDatabase;
    public static BackupScope SpecificSchemas => BackupScope.SpecificSchemas;
    public static BackupScope SpecificTables => BackupScope.SpecificTables;
}

public static class RestoreModeHelper
{
    public static RestoreMode Normal => RestoreMode.Normal;
    public static RestoreMode CleanAndRecreate => RestoreMode.CleanAndRecreate;
    public static RestoreMode DataOnly => RestoreMode.DataOnly;
}

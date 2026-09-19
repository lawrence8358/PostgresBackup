namespace PostgresBackup.Wpf.ViewModels;

/// <summary>
/// The user-visible lifecycle of a client-tool detection attempt.
/// This is deliberately separate from the completed core detection result.
/// </summary>
public enum ToolDetectionPresentationState
{
    NotChecked,
    Detecting,
    Ready,
    Incompatible,
    NotFound
}

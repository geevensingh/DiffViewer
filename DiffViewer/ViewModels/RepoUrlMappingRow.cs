using DiffViewer.Models;

namespace DiffViewer.ViewModels;

/// <summary>
/// Row VM used by <see cref="SettingsViewModel.RepoUrlMappings"/> to
/// render the "remembered clones" list in the Settings dialog. A record
/// (rather than an <see cref="System.ComponentModel.INotifyPropertyChanged"/>
/// shape) is enough because each row is immutable — Forget removes the
/// row and re-adds a fresh one if the user re-records the same mapping.
/// </summary>
public sealed record RepoUrlMappingRow(RepoUrlKey Key, string Path)
{
    /// <summary>
    /// Human-friendly key rendering used by the dialog's per-row
    /// <c>TextBlock</c> binding. Example:
    /// <c>github.com/geevensingh/jotjson</c>.
    /// </summary>
    public string DisplayKey => $"{Key.Host}/{Key.Owner}/{Key.Repo}";
}

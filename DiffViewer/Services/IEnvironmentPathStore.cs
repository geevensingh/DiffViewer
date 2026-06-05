namespace DiffViewer.Services;

/// <summary>
/// Seam over the persistent, per-user environment <c>PATH</c> value so
/// <see cref="UserPathRegistrar"/> can be unit-tested without touching the
/// real registry. The production implementation is
/// <see cref="WindowsUserPathStore"/>.
/// </summary>
internal interface IEnvironmentPathStore
{
    /// <summary>
    /// Reads the raw (unexpanded) user <c>PATH</c> and whether it is stored
    /// as <c>REG_EXPAND_SZ</c>. <c>Value</c> is <c>null</c> when no user
    /// <c>PATH</c> exists yet.
    /// </summary>
    (string? Value, bool IsExpandable) Read();

    /// <summary>
    /// Writes the user <c>PATH</c>, preserving the registry value kind
    /// (<c>REG_EXPAND_SZ</c> vs <c>REG_SZ</c>) via
    /// <paramref name="isExpandable"/>, and broadcasts an environment-change
    /// notification so newly launched processes observe the update.
    /// </summary>
    void Write(string value, bool isExpandable);
}

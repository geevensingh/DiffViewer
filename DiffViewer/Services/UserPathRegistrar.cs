using System;
using DiffViewer.Utility;

namespace DiffViewer.Services;

/// <summary>
/// Adds or removes the installed app's directory on the per-user <c>PATH</c>
/// so the executable is discoverable by name from any new terminal. Driven
/// by the Velopack install/update/uninstall hooks in <c>App.Main</c>.
/// </summary>
/// <remarks>
/// Idempotent: a no-op <see cref="Register"/> (entry already present) or
/// <see cref="Unregister"/> (entry absent) skips the underlying write and
/// its environment-change broadcast. This keeps the per-update hook cheap
/// and avoids re-broadcasting on every upgrade.
/// </remarks>
internal sealed class UserPathRegistrar
{
    private readonly IEnvironmentPathStore _store;

    public UserPathRegistrar(IEnvironmentPathStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Ensures <paramref name="directory"/> is on the user <c>PATH</c>.
    /// Returns <c>true</c> if a change was written.
    /// </summary>
    public bool Register(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;

        var (current, isExpandable) = _store.Read();
        var updated = PathListEditor.Add(current, directory);
        if (updated is null) return false;

        _store.Write(updated, isExpandable);
        return true;
    }

    /// <summary>
    /// Removes <paramref name="directory"/> from the user <c>PATH</c>.
    /// Returns <c>true</c> if a change was written.
    /// </summary>
    public bool Unregister(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;

        var (current, isExpandable) = _store.Read();
        var updated = PathListEditor.Remove(current, directory);
        if (updated is null) return false;

        _store.Write(updated, isExpandable);
        return true;
    }
}

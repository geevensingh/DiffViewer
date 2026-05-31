using System.Reflection;

namespace DiffViewer.Utility;

/// <summary>
/// Reads the running DiffViewer build's version for display in the
/// UI (Settings → Updates section). Two-method shape so the
/// reflection-based read is wrapped around a pure string-handling
/// inner method that's testable in isolation.
///
/// <para>Prefers <see cref="AssemblyInformationalVersionAttribute"/>
/// because that's what <c>release.yml</c> stamps with the release
/// tag (e.g. <c>1.6.0</c> or <c>1.6.0-rc1</c>), matching exactly
/// what users see on the GitHub Releases page. SourceLink may
/// append a <c>+gitHash</c> suffix to the informational version on
/// dev builds; we strip it for display so the value matches a tag
/// the user could navigate to.</para>
/// </summary>
public static class AppVersionInfo
{
    /// <summary>
    /// User-facing version string for the running DiffViewer build,
    /// e.g. <c>"1.6.0"</c> or <c>"1.6.0-rc1"</c>. Falls back to the
    /// raw <see cref="System.Reflection.AssemblyName.Version"/> shape
    /// (<c>"1.6.0.0"</c>) when no informational version is stamped,
    /// and to <c>"unknown"</c> when neither is available (defensive —
    /// should not happen in normal builds).
    /// </summary>
    public static string GetDisplayVersion()
    {
        var asm = Assembly.GetEntryAssembly();
        var info = asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var fallback = asm?.GetName().Version?.ToString();
        return GetDisplayVersionFromValues(info, fallback);
    }

    /// <summary>
    /// Testable inner overload. Takes the raw attribute values and
    /// produces the display string per the precedence rules
    /// described on the class. Public-internal (via
    /// <c>InternalsVisibleTo</c>) so the test project can drive it
    /// without round-tripping through reflection on a fake assembly.
    /// </summary>
    internal static string GetDisplayVersionFromValues(string? informationalVersion, string? assemblyVersion)
    {
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            // Strip SourceLink's "+gitHash" suffix so the displayed
            // value matches a tag the user could navigate to.
            var plus = informationalVersion!.IndexOf('+');
            return plus > 0 ? informationalVersion[..plus] : informationalVersion;
        }
        if (!string.IsNullOrWhiteSpace(assemblyVersion))
        {
            return assemblyVersion!;
        }
        return "unknown";
    }
}

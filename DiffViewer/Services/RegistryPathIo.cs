using System;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DiffViewer.Services;

/// <summary>
/// Low-level registry read/write for a <c>PATH</c>-style string value that
/// preserves the value kind. Extracted from <see cref="WindowsUserPathStore"/>
/// so the load-bearing <c>REG_EXPAND_SZ</c>-preservation logic can be tested
/// against a temporary key.
/// </summary>
/// <remarks>
/// The whole feature exists because .NET's
/// <see cref="Environment.GetEnvironmentVariable(string, EnvironmentVariableTarget)"/>
/// / <see cref="Environment.SetEnvironmentVariable(string, string, EnvironmentVariableTarget)"/>
/// pair expands <c>%VAR%</c> references on read and rewrites the value as
/// <c>REG_SZ</c> on write — silently corrupting a user <c>PATH</c> that uses
/// <c>%USERPROFILE%</c>-style entries. This helper reads with
/// <see cref="RegistryValueOptions.DoNotExpandEnvironmentNames"/> and writes
/// back with the original <see cref="RegistryValueKind"/>.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class RegistryPathIo
{
    /// <summary>
    /// Reads <paramref name="valueName"/> raw (no <c>%VAR%</c> expansion).
    /// Returns <c>(null, true)</c> when the value is absent — callers that
    /// create it should default to <c>REG_EXPAND_SZ</c>.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// The value exists but is not a string kind (<c>REG_SZ</c> /
    /// <c>REG_EXPAND_SZ</c>); the caller should fail safe rather than
    /// overwrite an unexpected value.
    /// </exception>
    public static (string? Value, bool IsExpandable) Read(RegistryKey key, string valueName)
    {
        var raw = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw is null) return (null, true);

        var kind = key.GetValueKind(valueName);
        if (kind != RegistryValueKind.String && kind != RegistryValueKind.ExpandString)
        {
            throw new NotSupportedException(
                $"Registry value '{valueName}' has unexpected kind {kind}; refusing to rewrite it.");
        }

        return (raw.ToString(), kind == RegistryValueKind.ExpandString);
    }

    /// <summary>
    /// Writes <paramref name="value"/> as <c>REG_EXPAND_SZ</c> when
    /// <paramref name="isExpandable"/> is <c>true</c>, else <c>REG_SZ</c>.
    /// </summary>
    public static void Write(RegistryKey key, string valueName, string value, bool isExpandable)
    {
        key.SetValue(
            valueName,
            value,
            isExpandable ? RegistryValueKind.ExpandString : RegistryValueKind.String);
    }
}

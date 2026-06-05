using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DiffViewer.Services;

/// <summary>
/// Production <see cref="IEnvironmentPathStore"/> backed by
/// <c>HKCU\Environment\Path</c>. Reads/writes the raw value (preserving
/// <c>REG_EXPAND_SZ</c>) via <see cref="RegistryPathIo"/> and broadcasts
/// <c>WM_SETTINGCHANGE</c> after a write so Explorer (and the new processes
/// it spawns) pick up the change without a sign-out.
/// </summary>
/// <remarks>
/// Already-running shells do not refresh — a terminal that was open before
/// the change must be restarted. This is thin OS-interop glue with no
/// branching logic; the testable parts live in <see cref="RegistryPathIo"/>
/// and <see cref="UserPathRegistrar"/>.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsUserPathStore : IEnvironmentPathStore
{
    private const string EnvironmentSubKey = "Environment";
    private const string PathValueName = "Path";

    public (string? Value, bool IsExpandable) Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(EnvironmentSubKey, writable: false);
        if (key is null) return (null, true);
        return RegistryPathIo.Read(key, PathValueName);
    }

    public void Write(string value, bool isExpandable)
    {
        using (var key = Registry.CurrentUser.OpenSubKey(EnvironmentSubKey, writable: true)
                         ?? Registry.CurrentUser.CreateSubKey(EnvironmentSubKey))
        {
            RegistryPathIo.Write(key, PathValueName, value, isExpandable);
        }

        BroadcastEnvironmentChange();
    }

    private static readonly IntPtr HWND_BROADCAST = new(0xffff);
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    private static void BroadcastEnvironmentChange()
    {
        try
        {
            // Short timeout: this runs inside a Velopack fast callback that
            // exits promptly. A hung top-level window must not stall the
            // installer; the registry change has already persisted.
            SendMessageTimeout(
                HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "Environment",
                SMTO_ABORTIFHUNG, 1000, out _);
        }
        catch
        {
            // Best-effort. Worst case, freshly launched terminals see the
            // change after the next sign-in instead of immediately.
        }
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint Msg, IntPtr wParam, string lParam,
        uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);
}

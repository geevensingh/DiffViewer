using System;
using System.Runtime.InteropServices;

namespace DiffViewer.Utility;

/// <summary>
/// Attaches the current process's standard streams to the parent process's
/// console (cmd.exe / PowerShell / git's stdio) when one is available. WPF
/// apps compile with <c>OutputType=WinExe</c> which does not allocate a
/// console; without this attach, <see cref="Console.Error"/> writes go
/// nowhere when the user launches DiffViewer from a terminal — including
/// the typical <c>git difftool</c> invocation that issue #5 targets.
/// </summary>
/// <remarks>
/// <para>Idempotent and best-effort: <see cref="AttachToParent"/> returns
/// the success flag from the underlying Win32 call. Returns <c>false</c>
/// for double-click launches (no parent console) and for re-entrant calls
/// after a console is already attached, which callers should treat as
/// "stderr writes are no-ops".</para>
///
/// <para>Why <c>ATTACH_PARENT_PROCESS</c> rather than <c>AllocConsole</c>:
/// allocating a brand-new console flashes a black window for non-CLI
/// launches. Attaching to the existing parent shares its window
/// (terminal, IDE integrated terminal, git's stdio handles) and produces
/// no visible artefact when no parent console exists.</para>
/// </remarks>
internal static class ConsoleAttacher
{
    private const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int dwProcessId);

    /// <summary>True iff a previous call to <see cref="AttachToParent"/> succeeded.</summary>
    public static bool IsAttached { get; private set; }

    /// <summary>
    /// Try to attach to the parent process's console. Safe to call once at
    /// startup; subsequent calls are no-ops and return the current attached
    /// state. Always returns <c>false</c> on non-Windows runtimes (which
    /// DiffViewer doesn't target, but the guard keeps unit testing simple).
    /// </summary>
    public static bool AttachToParent()
    {
        if (IsAttached) return true;
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            IsAttached = AttachConsole(ATTACH_PARENT_PROCESS);
        }
        catch
        {
            // Any P/Invoke failure (e.g. extreme sandbox) silently degrades
            // to "no stderr". The dialog path still works.
            IsAttached = false;
        }
        return IsAttached;
    }
}

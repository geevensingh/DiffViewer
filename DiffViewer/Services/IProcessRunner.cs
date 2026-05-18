namespace DiffViewer.Services;

/// <summary>
/// Thin seam over <see cref="System.Diagnostics.Process"/> so the
/// <see cref="GhCliAuthProvider"/> can be tested without spawning a real
/// process. Production code uses <see cref="DefaultProcessRunner"/>;
/// tests inject a fake that returns canned <see cref="ProcessRunResult"/>
/// values or throws <see cref="System.ComponentModel.Win32Exception"/> /
/// <see cref="System.IO.FileNotFoundException"/> to simulate
/// <c>gh</c>-not-on-PATH.
/// </summary>
internal interface IProcessRunner
{
    /// <summary>
    /// Runs <paramref name="fileName"/> with <paramref name="arguments"/>
    /// and returns the captured exit code, stdout, and stderr.
    /// </summary>
    /// <remarks>
    /// Throws <see cref="System.ComponentModel.Win32Exception"/> or
    /// <see cref="System.IO.FileNotFoundException"/> when the executable
    /// is not found on <c>PATH</c> (or the OS can't start the process for
    /// another reason). Throws
    /// <see cref="System.OperationCanceledException"/> if
    /// <paramref name="ct"/> fires before the process exits. All other
    /// exit shapes (non-zero exit code, empty stdout, garbage stderr) are
    /// surfaced as a returned <see cref="ProcessRunResult"/>, not thrown.
    /// </remarks>
    Task<ProcessRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken ct);
}

/// <summary>Result of an external-process run.</summary>
internal sealed record ProcessRunResult(int ExitCode, string Stdout, string Stderr);

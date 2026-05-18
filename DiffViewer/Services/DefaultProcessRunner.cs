using System.Diagnostics;
using System.Text;

namespace DiffViewer.Services;

/// <summary>
/// Production <see cref="IProcessRunner"/> wrapping
/// <see cref="Process.Start(ProcessStartInfo)"/>. Captures stdout/stderr,
/// waits for exit honoring the cancellation token, and lets
/// <see cref="System.ComponentModel.Win32Exception"/> /
/// <see cref="System.IO.FileNotFoundException"/> propagate so callers can
/// distinguish "executable missing" from "executable ran and failed".
/// </summary>
internal sealed class DefaultProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };

        // Process.Start throws Win32Exception when fileName is not found,
        // FileNotFoundException for some shell-execute paths. Let both
        // propagate; GhCliAuthProvider catches them.
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new ProcessRunResult(process.ExitCode, stdout, stderr);
    }
}

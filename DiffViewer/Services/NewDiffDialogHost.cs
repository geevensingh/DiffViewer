using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DiffViewer.Models;
using DiffViewer.ViewModels;
using DiffViewer.Views;

namespace DiffViewer.Services;

/// <summary>
/// Production <see cref="INewDiffDialogHost"/>: builds a
/// <see cref="NewDiffDialogViewModel"/>, shows it modally over the
/// currently-active main window, and returns the user's choice.
///
/// <para>Last-used-mode is remembered for the lifetime of the host
/// (i.e. for the session). It is not persisted to disk in v1 —
/// per-session memory was the locked-in design decision; a persisted
/// preference can be added later via <see cref="ISettingsService"/>.</para>
/// </summary>
public sealed class NewDiffDialogHost : INewDiffDialogHost
{
    private readonly DiffModeRegistry _registry;
    private readonly IDiffLaunchValidator _validator;
    private readonly Func<Window?> _ownerLookup;
    private string? _lastProviderId;

    public NewDiffDialogHost(
        DiffModeRegistry registry,
        IDiffLaunchValidator validator,
        Func<Window?> ownerLookup)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _ownerLookup = ownerLookup ?? throw new ArgumentNullException(nameof(ownerLookup));
    }

    public Task<DiffLaunchSource?> ShowAsync(string? prefilledRepoPath, CancellationToken ct = default)
    {
        var owner = _ownerLookup();
        var vm = new NewDiffDialogViewModel(_registry, _validator, prefilledRepoPath, _lastProviderId);
        var dialog = new NewDiffDialog(vm);
        if (owner is not null) dialog.Owner = owner;

        // External cancellation (e.g. shutdown) closes the dialog.
        using var ctReg = ct.Register(() => dialog.Dispatcher.BeginInvoke((Action)(() =>
        {
            try { dialog.Close(); } catch { /* best-effort */ }
        })));

        dialog.ShowDialog();
        _lastProviderId = vm.SelectedProvider?.Id ?? _lastProviderId;

        // If the dialog was force-closed before the VM completed (the
        // [X] window button bypasses our Cancel command), Completion is
        // still pending — synthesise null so the caller never hangs.
        if (vm.Completion.IsCompleted)
        {
            return vm.Completion;
        }
        return Task.FromResult<DiffLaunchSource?>(null);
    }
}

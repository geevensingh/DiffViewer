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
///
/// <para><b>Clipboard PR-URL detection (A1)</b>: at dialog-show time
/// the host reads the system clipboard once and tries to parse it as
/// a GitHub PR URL via <see cref="PullRequestRef.TryParse"/>. On
/// success it overrides the dialog's initial mode to
/// <see cref="GitHubPullRequestProvider.ProviderId"/> and pre-fills
/// the URL field — but it does <em>not</em> update
/// <see cref="_lastProviderId"/>, so the next dialog open without a
/// PR URL on the clipboard reverts to whatever the user was actually
/// working in.</para>
///
/// <para><b>MRU repo-path fallback (A3)</b>: when the caller doesn't
/// pass a <c>prefilledRepoPath</c> (cold-launch, no current diff),
/// the host falls back to the most-recent entry in
/// <see cref="IRecentContextsService.Current"/>. Local forms then
/// open with the repo field populated, cutting the new-diff
/// interaction by one field on the common "I just opened the app
/// and want to look at the same repo as last time" case.</para>
/// </summary>
public sealed class NewDiffDialogHost : INewDiffDialogHost
{
    private readonly DiffModeRegistry _registry;
    private readonly IDiffLaunchValidator _validator;
    private readonly IGitRefEnumerator _refEnumerator;
    private readonly IRecentContextsService _recentContexts;
    private readonly IClipboardService _clipboard;
    private readonly Func<Window?> _ownerLookup;
    private string? _lastProviderId;

    public NewDiffDialogHost(
        DiffModeRegistry registry,
        IDiffLaunchValidator validator,
        IGitRefEnumerator refEnumerator,
        IRecentContextsService recentContexts,
        IClipboardService clipboard,
        Func<Window?> ownerLookup)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _refEnumerator = refEnumerator ?? throw new ArgumentNullException(nameof(refEnumerator));
        _recentContexts = recentContexts ?? throw new ArgumentNullException(nameof(recentContexts));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _ownerLookup = ownerLookup ?? throw new ArgumentNullException(nameof(ownerLookup));
    }

    public Task<DiffLaunchSource?> ShowAsync(string? prefilledRepoPath, CancellationToken ct = default)
    {
        var (effectiveRepoPath, initialProviderIdOverride, clipboardPrUrl) =
            ComputeSeed(prefilledRepoPath, _clipboard, _recentContexts);

        var owner = _ownerLookup();
        var vm = new NewDiffDialogViewModel(
            _registry, _validator, _refEnumerator, _recentContexts,
            effectiveRepoPath,
            initialProviderIdOverride ?? _lastProviderId,
            clipboardPrUrl);
        var dialog = new NewDiffDialog(vm);
        if (owner is not null) dialog.Owner = owner;

        // External cancellation (e.g. shutdown) closes the dialog.
        using var ctReg = ct.Register(() => dialog.Dispatcher.BeginInvoke((Action)(() =>
        {
            try { dialog.Close(); } catch { /* best-effort */ }
        })));

        dialog.ShowDialog();
        UpdateLastProviderId(vm.SelectedProvider?.Id, initialProviderIdOverride);

        // If the dialog was force-closed before the VM completed (the
        // [X] window button bypasses our Cancel command), Completion is
        // still pending — synthesise null so the caller never hangs.
        if (vm.Completion.IsCompleted)
        {
            return vm.Completion;
        }
        return Task.FromResult<DiffLaunchSource?>(null);
    }

    /// <summary>
    /// Pure seed-computation seam exposed for unit testing the
    /// host's clipboard (A1) and MRU-repo-fallback (A3) decisions
    /// without spinning up a real WPF dialog.
    ///
    /// <para>Returns the three values fed into
    /// <see cref="NewDiffDialogViewModel"/>:</para>
    /// <list type="bullet">
    /// <item><c>RepoPath</c> — caller-supplied <paramref name="prefilledRepoPath"/>
    ///   or the most-recent MRU repo (A3) when null/empty.</item>
    /// <item><c>InitialProviderIdOverride</c> — non-null only when the
    ///   clipboard parses as a PR URL (A1). Production code then
    ///   prefers this over the session's last-used provider, but
    ///   does NOT persist it as the new last-used.</item>
    /// <item><c>ClipboardPrUrl</c> — same as the clipboard text when
    ///   it parses; null otherwise.</item>
    /// </list>
    /// </summary>
    internal static (string? RepoPath, string? InitialProviderIdOverride, string? ClipboardPrUrl)
        ComputeSeed(
            string? prefilledRepoPath,
            IClipboardService clipboard,
            IRecentContextsService recentContexts)
    {
        // A1: detect a PR URL on the clipboard. If present, *for this
        // open only* we point the dialog at the PR provider and seed
        // the URL field. The caller must NOT persist this as the new
        // last-used provider — that's the point of "for this open only".
        string? clipboardPrUrl = null;
        string? initialProviderIdOverride = null;
        if (clipboard.TryGetText(out var clipboardText)
            && PullRequestRef.TryParse(clipboardText, out _, out _))
        {
            clipboardPrUrl = clipboardText;
            initialProviderIdOverride = GitHubPullRequestProvider.ProviderId;
        }

        // A3: when the caller didn't pre-fill a repo path, fall back to
        // the most-recent launch's canonical repo path. Local forms
        // then open with the repo field populated; users hitting
        // "New diff" cold land on their most recent repo by default.
        var effectiveRepoPath = !string.IsNullOrWhiteSpace(prefilledRepoPath)
            ? prefilledRepoPath
            : (recentContexts.Current.Count > 0
                ? recentContexts.Current[0].Identity.CanonicalRepoPath
                : null);

        return (effectiveRepoPath, initialProviderIdOverride, clipboardPrUrl);
    }

    private void UpdateLastProviderId(string? selectedProviderId, string? initialProviderIdOverride)
    {
        // Only update _lastProviderId from a user-driven selection.
        // Clipboard-detected PR mode is "for this open only" — if we
        // recorded it here, the next open without a clipboard URL
        // would still snap to PR mode, contradicting the design call.
        if (initialProviderIdOverride is null)
        {
            _lastProviderId = selectedProviderId ?? _lastProviderId;
        }
        else if (selectedProviderId is not null
                 && !string.Equals(selectedProviderId, initialProviderIdOverride, StringComparison.Ordinal))
        {
            // User explicitly switched away from the clipboard-seeded
            // PR mode while the dialog was open — that's a positive
            // choice we record like any other.
            _lastProviderId = selectedProviderId;
        }
    }

    // For tests: probe the live last-used-provider value so the
    // "clipboard doesn't poison session memory" invariant can be
    // asserted directly. Production callers don't need this.
    internal string? LastProviderIdForTests => _lastProviderId;

    // For tests: drive UpdateLastProviderId without standing up a
    // real WPF dialog. Production callers call ShowAsync, which
    // routes through this method.
    internal void UpdateLastProviderIdForTests(string? selectedProviderId, string? initialProviderIdOverride)
        => UpdateLastProviderId(selectedProviderId, initialProviderIdOverride);
}


using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DiffViewer.Models;
using DiffViewer.ViewModels;
using DiffViewer.Views;

namespace DiffViewer.Services;

/// <summary>
/// Production <see cref="IMissingClonePromptHost"/>: builds a
/// <see cref="MissingClonePromptViewModel"/> with all its production
/// dependencies, shows it modally over the currently-active main window,
/// and returns the user's choice.
/// </summary>
public sealed class MissingClonePromptHost : IMissingClonePromptHost
{
    private readonly ISettingsService _settings;
    private readonly IRepoInspector _inspector;
    private readonly IGitHubCloner _cloner;
    private readonly Func<Window?> _ownerLookup;

    public MissingClonePromptHost(
        ISettingsService settings,
        IRepoInspector inspector,
        IGitHubCloner cloner,
        Func<Window?> ownerLookup)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        _cloner = cloner ?? throw new ArgumentNullException(nameof(cloner));
        _ownerLookup = ownerLookup ?? throw new ArgumentNullException(nameof(ownerLookup));
    }

    public Task<MissingClonePromptResult> ShowAsync(PullRequestRef pr, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pr);

        var owner = _ownerLookup();
        var vm = new MissingClonePromptViewModel(
            pr,
            _settings,
            _inspector,
            _cloner,
            pickFolder: initial => PickFolder(owner, initial),
            confirmUseUnmatchedRemote: question => ConfirmYesNo(owner, "DiffViewer", question),
            confirmRememberDefaultClone: parent => ConfirmYesNo(
                owner,
                "Default clone destination",
                $"Remember \"{parent}\" as the default destination for future clones?"));

        var dialog = new MissingClonePromptDialog(vm);
        if (owner is not null) dialog.Owner = owner;

        // Cancellation from the coordinator (e.g. app shutdown) should
        // close the dialog. The VM owns clone-in-flight cancellation
        // internally; this registration handles the outer modal lifetime.
        using var ctReg = ct.Register(() => dialog.Dispatcher.BeginInvoke((Action)(() =>
        {
            // ShowDialog blocks; calling Close from another callback
            // unblocks the modal. If the user already accepted/cancelled
            // the dialog is already closed and Close is a no-op.
            try { dialog.Close(); } catch { /* best-effort */ }
        })));

        dialog.ShowDialog();

        // If the dialog was forcibly closed by cancellation before the
        // VM completed (rare; user pressed [X] on the window chrome),
        // Completion is still pending — race it against a synthesized
        // Cancelled so the coordinator never hangs.
        if (vm.Completion.IsCompleted)
        {
            return vm.Completion;
        }
        return Task.FromResult<MissingClonePromptResult>(new MissingClonePromptResult.Cancelled());
    }

    private static string? PickFolder(Window? owner, string? initial)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Pick a folder",
            Multiselect = false,
        };
        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
        {
            picker.InitialDirectory = initial;
        }
        var ok = owner is null ? picker.ShowDialog() : picker.ShowDialog(owner);
        return ok == true ? picker.FolderName : null;
    }

    private static bool ConfirmYesNo(Window? owner, string title, string message)
    {
        var result = owner is null
            ? MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }
}

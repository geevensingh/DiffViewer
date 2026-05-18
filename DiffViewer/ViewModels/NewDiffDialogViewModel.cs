using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffViewer.Models;
using DiffViewer.Services;

namespace DiffViewer.ViewModels;

/// <summary>
/// View-model for the "New diff" dialog. Hosts the left-rail mode
/// picker (<see cref="Providers"/>), the right-pane per-mode form
/// (<see cref="CurrentForm"/>, swapped in via implicit
/// <c>DataTemplate</c>), and the footer status + OK/Cancel.
///
/// <para><b>Completion pattern</b>: same as
/// <see cref="MissingClonePromptViewModel"/>. The dialog host calls
/// <c>ShowDialog()</c> and then <c>await</c>s <see cref="Completion"/>,
/// which resolves to the chosen <see cref="DiffLaunchSource"/> or
/// <c>null</c> on cancel.</para>
///
/// <para><b>Form caching</b>: <see cref="CurrentForm"/> is cached per
/// provider, so switching modes and back preserves partial input.
/// Forms are created lazily on first selection through
/// <see cref="IDiffModeProvider.CreateForm"/>.</para>
/// </summary>
public sealed partial class NewDiffDialogViewModel : ObservableObject
{
    private readonly IDiffLaunchValidator _validator;
    private readonly string? _prefilledRepoPath;
    private readonly Dictionary<IDiffModeProvider, NewDiffFormViewModelBase> _formCache = new();
    private readonly TaskCompletionSource<DiffLaunchSource?> _tcs;

    public IReadOnlyList<IDiffModeProvider> Providers { get; }

    public string Title => "New diff";

    public NewDiffDialogViewModel(
        DiffModeRegistry registry,
        IDiffLaunchValidator validator,
        string? prefilledRepoPath = null,
        string? initialProviderId = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _prefilledRepoPath = prefilledRepoPath;
        Providers = registry.Providers;

        if (Providers.Count == 0)
        {
            throw new ArgumentException("DiffModeRegistry must have at least one provider.", nameof(registry));
        }

        _tcs = new TaskCompletionSource<DiffLaunchSource?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Honor the caller-supplied initial selection (in v1 used to
        // restore the previously-used mode per-session); fall back to
        // the first registered provider so the dialog always opens with
        // some form visible.
        _selectedProvider = Providers[0];
        if (initialProviderId is not null)
        {
            foreach (var p in Providers)
            {
                if (string.Equals(p.Id, initialProviderId, StringComparison.Ordinal))
                {
                    _selectedProvider = p;
                    break;
                }
            }
        }

        AttachFormHandlers(CurrentForm);
    }

    /// <summary>
    /// Completes with the chosen launch source on OK, or <c>null</c>
    /// on Cancel (or when the dialog is force-closed by cancellation).
    /// </summary>
    public Task<DiffLaunchSource?> Completion => _tcs.Task;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentForm))]
    [NotifyPropertyChangedFor(nameof(IsOkEnabled))]
    private IDiffModeProvider _selectedProvider;

    partial void OnSelectedProviderChanged(IDiffModeProvider? oldValue, IDiffModeProvider newValue)
    {
        if (oldValue is not null && _formCache.TryGetValue(oldValue, out var oldForm))
        {
            oldForm.PropertyChanged -= OnFormPropertyChanged;
        }
        AttachFormHandlers(CurrentForm);
        OkCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The form bound into the dialog's right pane. Cached per provider.</summary>
    public NewDiffFormViewModelBase CurrentForm
    {
        get
        {
            if (!_formCache.TryGetValue(SelectedProvider, out var form))
            {
                form = SelectedProvider.CreateForm(_validator, _prefilledRepoPath);
                _formCache[SelectedProvider] = form;
            }
            return form;
        }
    }

    public bool IsOkEnabled => CurrentForm.IsValid;

    [RelayCommand(CanExecute = nameof(IsOkEnabled))]
    private void Ok()
    {
        if (!CurrentForm.IsValid) return;
        var source = CurrentForm.BuildLaunchSource();
        _tcs.TrySetResult(source);
    }

    [RelayCommand]
    private void Cancel() => _tcs.TrySetResult(null);

    /// <summary>
    /// Force-cancel the dialog from outside (e.g. the view's
    /// <c>Closing</c> handler when the user hits the [X] window button).
    /// Idempotent.
    /// </summary>
    public void ForceCancel() => _tcs.TrySetResult(null);

    private void AttachFormHandlers(NewDiffFormViewModelBase form)
    {
        form.PropertyChanged -= OnFormPropertyChanged;
        form.PropertyChanged += OnFormPropertyChanged;
    }

    private void OnFormPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Any form-property change might shift validity; cheaper to
        // unconditionally re-notify than to filter property names and
        // forget to update the list when a form adds a new field.
        OnPropertyChanged(nameof(IsOkEnabled));
        OkCommand.NotifyCanExecuteChanged();
    }
}

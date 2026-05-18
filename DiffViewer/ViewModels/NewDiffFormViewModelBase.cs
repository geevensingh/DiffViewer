using System;
using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;
using DiffViewer.Services;

namespace DiffViewer.ViewModels;

/// <summary>
/// Base class for per-mode forms inside the "New diff" dialog.
///
/// <para><b>State machine</b>: each concrete form owns the bindable
/// properties for its inputs and re-runs <see cref="Validate"/> from
/// every <c>OnXChanged</c> partial. The base exposes
/// <see cref="ValidationError"/> (null = OK, non-null = shown in the
/// dialog footer in red) and <see cref="IsValid"/> (derived; drives
/// the OK button's <c>CanExecute</c>). Forms that need an extra
/// "required field empty" gate beyond what <see cref="ComputeValidationError"/>
/// returns can override <see cref="HasRequiredInputs"/>; the default
/// returns <c>true</c>.</para>
///
/// <para>Construction-time pre-fill: forms may seed their inputs from
/// <paramref name="currentContext"/> (the active diff in the main
/// window, or <c>null</c> on cold-launch). This is what makes "compare
/// another two commits in this repo" a one-field interaction.</para>
/// </summary>
public abstract partial class NewDiffFormViewModelBase : ObservableObject
{
    private readonly IDiffLaunchValidator _validator;

    protected NewDiffFormViewModelBase(IDiffLaunchValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    protected IDiffLaunchValidator Validator => _validator;

    /// <summary>
    /// Human-readable validation error to surface in the dialog footer,
    /// or <c>null</c> when the form is in a launchable state. Drives
    /// <see cref="IsValid"/>; concrete forms write this from their
    /// per-property change handlers via <see cref="Validate"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string? _validationError;

    /// <summary>
    /// <c>true</c> when the dialog should enable OK for this form.
    /// Combines "no error" with the form-specific
    /// <see cref="HasRequiredInputs"/> gate — empty required fields
    /// disable OK without surfacing an error message (a friendlier UX
    /// than "Repository path is empty." flashing on every keystroke).
    /// </summary>
    public bool IsValid => ValidationError is null && HasRequiredInputs;

    /// <summary>
    /// Override to declare whether every required input is non-empty.
    /// Default: always <c>true</c> (form has no required-empty gate).
    /// </summary>
    protected virtual bool HasRequiredInputs => true;

    /// <summary>
    /// Compute the current validation error string, or <c>null</c> when
    /// the form is launchable. Called by <see cref="Validate"/>; concrete
    /// forms invoke <see cref="Validate"/> from every property change
    /// that affects validity.
    /// </summary>
    protected abstract string? ComputeValidationError();

    /// <summary>
    /// Re-run validation. Forms must call this from every property
    /// setter that affects <see cref="ComputeValidationError"/> or
    /// <see cref="HasRequiredInputs"/>; the base writes
    /// <see cref="ValidationError"/> and forces an <see cref="IsValid"/>
    /// notification.
    /// </summary>
    protected void Validate()
    {
        ValidationError = ComputeValidationError();
        OnPropertyChanged(nameof(IsValid));
    }

    /// <summary>
    /// Produce the <see cref="DiffLaunchSource"/> the dialog should
    /// hand back to <see cref="IContextSwitcher.SwitchToAsync"/>.
    /// Callers must check <see cref="IsValid"/> first; behavior when
    /// the form is invalid is undefined (concrete forms may throw).
    /// </summary>
    public abstract DiffLaunchSource BuildLaunchSource();
}

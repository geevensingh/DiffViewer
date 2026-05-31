using System;
using System.Globalization;
using System.Windows.Data;
using DiffViewer.Models;

namespace DiffViewer.Utility;

/// <summary>
/// <see cref="IValueConverter"/> that maps the user-facing auto-update
/// enums to friendly display strings, so the
/// <c>SettingsDialog</c> dropdowns read as "Every six hours" rather
/// than the raw <c>EverySixHours</c> identifier.
///
/// <para>Stateless singleton — bind via
/// <c>{x:Static util:EnumDisplayConverter.Instance}</c>. <see cref="ConvertBack"/>
/// is a no-op (returns <see cref="Binding.DoNothing"/>) because the
/// converter is only used on the display path of a ComboBox
/// <c>ItemTemplate</c>; the underlying enum value is what
/// <c>SelectedItem</c> binds to and round-trips through.</para>
/// </summary>
public sealed class EnumDisplayConverter : IValueConverter
{
    public static readonly EnumDisplayConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        AutoUpdateMode.Automatic => "Automatic (silent install)",
        AutoUpdateMode.NotifyOnly => "Notify only (show banner)",
        AutoUpdateMode.Disabled => "Disabled",

        UpdateCheckCadence.StartupOnly => "Startup only",
        UpdateCheckCadence.Hourly => "Hourly",
        UpdateCheckCadence.EverySixHours => "Every six hours",
        UpdateCheckCadence.Daily => "Daily",
        UpdateCheckCadence.Weekly => "Weekly",

        _ => value?.ToString() ?? string.Empty,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

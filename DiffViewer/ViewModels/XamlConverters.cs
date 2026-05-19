using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DiffViewer.ViewModels;

/// <summary>XAML helper: inverts a <see cref="bool"/>. Used by the
/// Settings dialog to grey out the color-scheme dropdown when a
/// hand-edited custom palette is in effect.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}

/// <summary>
/// Maps a <see cref="bool"/> to a <see cref="GridLength"/>: <c>true</c>
/// becomes the length parsed from <c>ConverterParameter</c> (XAML
/// syntax — <c>"5"</c> → 5 pixels, <c>"5*"</c> → 5 star units,
/// <c>"*"</c> or null → 1 star, <c>"Auto"</c> → auto), and
/// <c>false</c> becomes <c>0</c>. Used to collapse side-by-side diff
/// columns when the user hides one side via the toolbar's
/// side-visibility toggle without pulling
/// <see cref="System.Windows.GridLength"/> into the view-model.
/// </summary>
public sealed class BoolToGridLengthConverter : IValueConverter
{
    public static readonly BoolToGridLengthConverter Instance = new();
    private static readonly GridLengthConverter GridLengthParser = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not bool visible || !visible)
            return new GridLength(0);

        if (parameter is string s && !string.IsNullOrWhiteSpace(s))
        {
            try
            {
                var parsed = GridLengthParser.ConvertFromString(null!, CultureInfo.InvariantCulture, s);
                if (parsed is GridLength gl) return gl;
            }
            catch (FormatException) { /* fall through to default */ }
            catch (NotSupportedException) { /* fall through to default */ }
        }

        return new GridLength(1, GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Two-way XAML helper for binding a <see cref="System.Windows.Controls.Primitives.ToggleButton"/>
/// to one value of an enum. <c>Convert</c> returns <c>true</c> when the
/// source enum equals <c>ConverterParameter</c>; <c>ConvertBack</c>
/// returns the parameter value when <c>IsChecked</c> goes true and
/// <see cref="Binding.DoNothing"/> otherwise. The "do nothing" branch
/// is what makes a group of <see cref="ToggleButton"/>s behave as a
/// radio group: unchecking the currently-checked button by itself
/// would otherwise blow away the enum's value with a no-op.
/// </summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public static readonly EnumToBoolConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return false;
        var target = ResolveParameter(value.GetType(), parameter);
        return target is not null && value.Equals(target);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is not null)
        {
            var target = ResolveParameter(targetType, parameter);
            if (target is not null) return target;
        }
        return Binding.DoNothing;
    }

    private static object? ResolveParameter(Type enumType, object parameter)
    {
        if (!enumType.IsEnum)
        {
            // Bindings on nullable enums hand us the underlying type.
            var underlying = Nullable.GetUnderlyingType(enumType);
            if (underlying is null || !underlying.IsEnum) return null;
            enumType = underlying;
        }

        if (parameter.GetType() == enumType) return parameter;
        if (parameter is string s && Enum.TryParse(enumType, s, ignoreCase: true, out var parsed))
            return parsed;
        return null;
    }
}

/// <summary>XAML helper: <see cref="int"/>-valued <c>Count</c> bindings
/// become <see cref="Visibility.Visible"/> when zero, otherwise
/// <see cref="Visibility.Collapsed"/>. Used by the Settings dialog to
/// show "no remembered clones yet" placeholder text only when the
/// <c>RepoUrlMappings</c> collection is empty.</summary>
public sealed class ZeroToVisibleConverter : IValueConverter
{
    public static readonly ZeroToVisibleConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i && i == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>XAML helper: an <see cref="int"/> <c>Count</c> &gt; 0 maps
/// to <see cref="Visibility.Visible"/>, otherwise
/// <see cref="Visibility.Collapsed"/>. Used by the ref-picker popup
/// to hide a group's header + list when the filtered collection is
/// empty without flickering an empty header row.</summary>
public sealed class PositiveCountToVisibleConverter : IValueConverter
{
    public static readonly PositiveCountToVisibleConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i && i > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>XAML helper: a <see cref="bool"/> <c>false</c> maps to
/// <see cref="Visibility.Visible"/>, otherwise
/// <see cref="Visibility.Collapsed"/>. Used by the ref-picker popup
/// to show the "no refs match" empty-state hint only when none of
/// the four groups have visible entries.</summary>
public sealed class FalseToVisibleConverter : IValueConverter
{
    public static readonly FalseToVisibleConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is false ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>XAML helper: any non-null, non-empty string maps to
/// <see cref="Visibility.Visible"/>, otherwise
/// <see cref="Visibility.Collapsed"/>. Used by the ref-picker popup's
/// merge-base composer to surface error text only when a real error
/// is present.</summary>
public sealed class NonEmptyStringToVisibleConverter : IValueConverter
{
    public static readonly NonEmptyStringToVisibleConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// XAML helper: maps an enum value to <see cref="Visibility.Visible"/>
/// when it equals <c>ConverterParameter</c>, otherwise
/// <see cref="Visibility.Collapsed"/>. Used by the image-diff view to
/// switch between SideBySide / Swipe / OnionSkin layouts without
/// declaring three DataTriggers per layout. Mirrors
/// <see cref="EnumToBoolConverter"/>'s parameter-resolution logic so
/// callers can pass either a typed enum value (via <c>x:Static</c>)
/// or a case-insensitive string.
/// </summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public static readonly EnumToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return Visibility.Collapsed;
        var target = ResolveParameter(value.GetType(), parameter);
        return target is not null && value.Equals(target)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static object? ResolveParameter(Type enumType, object parameter)
    {
        if (!enumType.IsEnum)
        {
            var underlying = Nullable.GetUnderlyingType(enumType);
            if (underlying is null || !underlying.IsEnum) return null;
            enumType = underlying;
        }
        if (parameter.GetType() == enumType) return parameter;
        if (parameter is string s && Enum.TryParse(enumType, s, ignoreCase: true, out var parsed))
            return parsed;
        return null;
    }
}

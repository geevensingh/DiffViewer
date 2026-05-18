using System.Globalization;
using System.Windows.Data;

namespace DiffViewer.ViewModels;

/// <summary>
/// Maps a string to a bool that is <c>true</c> when the string is
/// non-null and non-empty. Used by <c>FileListView</c> to drive the
/// filter clear-X button's visibility off the <c>FilterText</c>
/// property — the button should appear only when there's something
/// to clear.
/// </summary>
public sealed class StringNotEmptyToBoolConverter : IValueConverter
{
    public static readonly StringNotEmptyToBoolConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

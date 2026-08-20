using System.Globalization;
using System.Windows.Data;

namespace QuotaBar.Win.Converters;

public sealed class FontScaleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (!double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var baseSize))
            return 0d;

        var scale = value is double fontScale ? Normalize(fontScale) : 1d;
        return baseSize * scale;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;

    public static double Normalize(double fontScale) => fontScale is 1.25 or 1.5 ? fontScale : 1d;
}

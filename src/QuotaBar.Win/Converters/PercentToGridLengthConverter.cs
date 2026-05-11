using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace QuotaBar.Win.Converters;

public class PercentToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double percent)
        {
            return new GridLength(percent, GridUnitType.Star);
        }
        return new GridLength(0, GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

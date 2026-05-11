using System.Globalization;
using System.Windows.Data;
using QuotaBar.Core.Models;

namespace QuotaBar.Win.Converters;

public class SpeedStatusToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SpeedStatus status)
        {
            return status switch
            {
                SpeedStatus.Fast => "FAST",
                SpeedStatus.Normal => "OK",
                SpeedStatus.Slow => "SLOW",
                _ => ""
            };
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

using System.Globalization;
using System.Windows.Data;
using QuotaBar.Core.Models;

namespace QuotaBar.Win.Converters;

public class SpeedStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SpeedStatus status)
        {
            return status switch
            {
                SpeedStatus.Fast => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x33, 0x33)),
                SpeedStatus.Normal => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xBF, 0x00)),
                SpeedStatus.Slow => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xCC, 0x66)),
                _ => System.Windows.Media.Brushes.Gray
            };
        }
        return System.Windows.Media.Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

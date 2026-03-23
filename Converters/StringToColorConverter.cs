using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DmPayQuery.Converters;

[ValueConversion(typeof(string), typeof(Brush))]
public class StringToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var colorName = value?.ToString() ?? "Black";
        return colorName switch
        {
            "Red" => Brushes.Red,
            "Green" => Brushes.Green,
            "Blue" => Brushes.Blue,
            "Cyan" => new SolidColorBrush(Color.FromRgb(0, 188, 212)),
            "Orange" => Brushes.Orange,
            "Purple" => Brushes.Purple,
            _ => Brushes.Black
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
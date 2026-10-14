using System.Globalization;
using System.Windows.Data;

namespace GoodieBag.Controls;

public sealed class KnobOffset : IValueConverter
{
    public const double Travel = 18;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Travel : 0.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

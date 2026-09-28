using System.Globalization;
using System.Windows.Data;

namespace CaptionTranslator
{
    /// <summary>Multiplies a number by the converter parameter, e.g. the German line at 75 % of the English font size.</summary>
    public sealed class ScaleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double factor = double.Parse((string)parameter, CultureInfo.InvariantCulture);
            return value is double number ? Math.Max(9, number * factor) : value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}

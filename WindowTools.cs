using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace WindowTools
{
    [ValueConversion(typeof(string), typeof(string))]
    public class RatioConverter : MarkupExtension, IValueConverter
    {
        private static RatioConverter _instance;

        public RatioConverter() { }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        { // do not let the culture default to local to prevent variable outcome re decimal syntax
            double size = System.Convert.ToDouble(value) * System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture);
            return size.ToString("G0", CultureInfo.InvariantCulture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        { // read only converter...
            throw new NotImplementedException();
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return _instance ?? (_instance = new RatioConverter());
        }

    }

    public class ComboBoxExtensions
    {
        public static readonly DependencyProperty ButtonFontFamilyProperty =
            DependencyProperty.RegisterAttached(
                "ButtonFontFamily",
                typeof(FontFamily),
                typeof(ComboBoxExtensions),
                new PropertyMetadata(SystemFonts.MessageFontFamily));

        public static void SetButtonFontFamily(UIElement element, FontFamily value)
        {
            element.SetValue(ButtonFontFamilyProperty, value);
        }

        public static FontFamily GetButtonFontFamily(UIElement element)
        {
            return (FontFamily)element.GetValue(ButtonFontFamilyProperty);
        }

        public static readonly DependencyProperty ButtonFontSizeProperty =
            DependencyProperty.RegisterAttached(
                "ButtonFontSize",
                typeof(double),
                typeof(ComboBoxExtensions),
                new PropertyMetadata(SystemFonts.MessageFontSize));

        public static void SetButtonFontSize(UIElement element, double value)
        {
            element.SetValue(ButtonFontSizeProperty, value);
        }

        public static double GetButtonFontSize(UIElement element)
        {
            return (double)element.GetValue(ButtonFontSizeProperty);
        }
    }
}
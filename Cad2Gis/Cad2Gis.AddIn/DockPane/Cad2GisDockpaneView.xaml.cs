using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Cad2Gis.AddIn.DockPane;

public partial class Cad2GisDockpaneView : UserControl
{
    public Cad2GisDockpaneView()
    {
        InitializeComponent();
    }
}

public class GeometryToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string geom && parameter is string target)
        {
            if (string.Equals(geom, target, StringComparison.OrdinalIgnoreCase))
                return Visibility.Visible;
            if (target.Equals("Polygon", StringComparison.OrdinalIgnoreCase) &&
                (geom.Equals("Hatch", StringComparison.OrdinalIgnoreCase) || geom.Equals("ClosedLine", StringComparison.OrdinalIgnoreCase)))
                return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

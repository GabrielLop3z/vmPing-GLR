using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using vmPing.Properties;

namespace vmPing.Classes
{
    public class BoolToValueConverter<T> : IValueConverter
    {
        public T FalseValue { get; set; }
        public T TrueValue { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool v && v
                ? TrueValue
                : FalseValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null && value.Equals(TrueValue);
        }
    }

    public class BoolToStringConverter : BoolToValueConverter<string> { }

    public class BooleanToHiddenVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool v && v)
                ? Visibility.Visible
                : Visibility.Hidden;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool v && v);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool v && v)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class InverseHiddenToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Returns true if visibility is hidden or collapsed.
            if (value is Visibility v)
            {
                return v == Visibility.Hidden || v == Visibility.Collapsed;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class BooleanToImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool v && !v)
            {
                return (DrawingImage)Application.Current.Resources["icon.play"];
            }
            else
            {
                return (DrawingImage)Application.Current.Resources["icon.stop-circle"];
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    internal static class ProbeBrushHelper
    {
        private static readonly BrushConverter BrushConverter = new BrushConverter();

        public static Brush FromColorString(string color)
        {
            try
            {
                var brush = BrushConverter.ConvertFromString(color) as Brush;
                return brush ?? Brushes.Transparent;
            }
            catch
            {
                return Brushes.Transparent;
            }
        }
    }

    public class ProbeStatusToBackgroundBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is ProbeStatus))
            {
                return Brushes.Transparent;
            }

            string colorString;
            switch ((ProbeStatus)value)
            {
                case ProbeStatus.Up:
                    colorString = ApplicationOptions.BackgroundColor_Probe_Up;
                    return CreateGradientBrush(colorString);
                case ProbeStatus.Down:
                    colorString = ApplicationOptions.BackgroundColor_Probe_Down;
                    return CreateGradientBrush(colorString);
                case ProbeStatus.Error:
                    colorString = ApplicationOptions.BackgroundColor_Probe_Error;
                    return CreateGradientBrush(colorString);
                case ProbeStatus.LatencyHigh:
                case ProbeStatus.Indeterminate:
                    colorString = ApplicationOptions.BackgroundColor_Probe_Indeterminate;
                    return CreateGradientBrush(colorString);
                case ProbeStatus.Scanner:
                    colorString = ApplicationOptions.BackgroundColor_Probe_Scanner;
                    return CreateGradientBrush(colorString);
                default:
                    colorString = ApplicationOptions.BackgroundColor_Probe_Inactive;
                    return ProbeBrushHelper.FromColorString(colorString);
            }
        }

        private Brush CreateGradientBrush(string baseColor)
        {
            Color color = (Color)ColorConverter.ConvertFromString(baseColor);
            
            // Adjust opacity if in Crystal or Colorful mode
            if (ApplicationOptions.CurrentTheme == ApplicationOptions.AppTheme.Crystal || 
                ApplicationOptions.CurrentTheme == ApplicationOptions.AppTheme.Colorful)
            {
                color.A = 180; // Semi-transparent
            }

            // Create a lighter version for the top of the gradient
            Color lightColor = Color.FromArgb(
                color.A,
                (byte)Math.Min(255, color.R + 25),
                (byte)Math.Min(255, color.G + 25),
                (byte)Math.Min(255, color.B + 25));

            return new LinearGradientBrush(lightColor, color, new Point(0, 0), new Point(0, 1));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class ProbeStatusToForegroundBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is ProbeStatus))
            {
                return Brushes.Transparent;
            }

            switch ((ProbeStatus)value)
            {
                case ProbeStatus.Up:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Probe_Up);
                case ProbeStatus.Down:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Probe_Down);
                case ProbeStatus.Error:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Probe_Error);
                case ProbeStatus.LatencyHigh:
                case ProbeStatus.Indeterminate:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Probe_Indeterminate);
                case ProbeStatus.Scanner:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Probe_Scanner);
                default:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Probe_Inactive);
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class ProbeStatusToStatisticsBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is ProbeStatus))
            {
                return Brushes.Transparent;
            }

            switch ((ProbeStatus)value)
            {
                case ProbeStatus.Up:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Stats_Up);
                case ProbeStatus.Down:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Stats_Down);
                case ProbeStatus.Error:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Stats_Error);
                case ProbeStatus.LatencyHigh:
                case ProbeStatus.Indeterminate:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Stats_Indeterminate);
                default:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Stats_Inactive);
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class ProbeStatusToAliasBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is ProbeStatus))
            {
                return Brushes.Transparent;
            }

            switch ((ProbeStatus)value)
            {
                case ProbeStatus.Up:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Alias_Up);
                case ProbeStatus.Down:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Alias_Down);
                case ProbeStatus.Error:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Alias_Error);
                case ProbeStatus.LatencyHigh:
                case ProbeStatus.Indeterminate:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Alias_Indeterminate);
                case ProbeStatus.Scanner:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Alias_Scanner);
                default:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Alias_Inactive);
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class StringToBrushConverter : IValueConverter
    {
        private static readonly BrushConverter BrushConverter = new BrushConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var str = value as string;
            if (string.IsNullOrWhiteSpace(str))
            {
                return Binding.DoNothing;
            }

            try
            {
                var brush = BrushConverter.ConvertFromString(str) as Brush;
                return brush ?? Binding.DoNothing;
            }
            catch
            {
                return Binding.DoNothing;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class HostnameFontsizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is double))
            {
                return 12.5;
            }

            double width = (double)value;

            if (width > 250) return 18;
            if (width > 225) return 17;
            if (width > 200) return 16;
            if (width > 175) return 15;
            if (width > 150) return 14;
            if (width > 125) return 13;
            return 12.5;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class ButtonTextVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is double v && v > 300)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class ProbeStatusToGlyphConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is ProbeStatus))
            {
                return string.Empty;
            }

            switch ((ProbeStatus)value)
            {
                case ProbeStatus.Up:
                    return "t";
                case ProbeStatus.Down:
                    return "u";
                case ProbeStatus.LatencyHigh:
                case ProbeStatus.Indeterminate:
                    return "i";
                default:
                    return string.Empty;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }


    public class ProbeCountToGlobalStartStopText : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            long count = (value is long v) ? v : 0;
            return count > 0
                ? "Detener Todo"
                : "Iniciar Todo";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class ProbeCountToGlobalStartStopIcon : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            long count = (value is long v) ? v : 0;
            return count > 0
                ? (DrawingImage)Application.Current.Resources["icon.stop-circle"]
                : (DrawingImage)Application.Current.Resources["icon.play"];
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class ProbeTypeToFontSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is ProbeType))
            {
                return ApplicationOptions.FontSize_Probe;
            }

            switch ((ProbeType)value)
            {
                case ProbeType.Ping:
                    return ApplicationOptions.FontSize_Probe;
                default:
                    return ApplicationOptions.FontSize_Scanner;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class StringLengthToBoolConverter : IValueConverter
    {
        // Return true if string is not empty.
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !string.IsNullOrEmpty(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class LatencyHistoryToPointsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var history = value as System.Collections.ObjectModel.ObservableCollection<int>;
            if (history == null || history.Count < 2) return null;

            double width = 100;
            double height = 25;
            
            var points = new PointCollection();
            double stepX = width / (Math.Max(1, history.Count - 1));
            
            int localMax = 0;
            foreach(int v in history) if (v > localMax) localMax = v;
            if (localMax < 50) localMax = 100;

            for (int i = 0; i < history.Count; i++)
            {
                int val = history[i];
                if (val < 0) val = localMax; // Show loss as peak
                
                double x = i * stepX;
                double y = height - ((double)Math.Min(val, localMax) / localMax * height);
                points.Add(new Point(x, y));
            }
            return points;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}

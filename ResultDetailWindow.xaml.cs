using System;
using System.Windows;
using System.Windows.Data;
using DiskBenchmark.Models;

namespace DiskBenchmark
{
    public partial class ResultDetailWindow : Window
    {
        public ResultDetailWindow(BenchmarkResult result)
        {
            InitializeComponent();
            
            // 确保Config不为null
            if (result != null && result.Config == null)
            {
                result.Config = new BenchmarkConfig();
            }
            
            DataContext = result;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class FileSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null) return "--";
            
            if (int.TryParse(value.ToString(), out int mb))
            {
                if (mb >= 1024)
                {
                    double gb = mb / 1024.0;
                    return $"{gb:F2} GB";
                }
                else
                {
                    return $"{mb} MB";
                }
            }
            
            return value?.ToString() ?? "--";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}


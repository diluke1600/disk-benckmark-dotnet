using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using DiskBenchmark.Models;
using DiskBenchmark.Services;

namespace DiskBenchmark
{
    public partial class HistoryWindow : Window
    {
        private readonly HistoryService _historyService;

        public HistoryWindow(HistoryService historyService)
        {
            InitializeComponent();
            _historyService = historyService;
            LoadHistory();
        }

        private void LoadHistory()
        {
            HistoryDataGrid.ItemsSource = _historyService.GetHistory();
        }

        private void HistoryDataGrid_LoadingRow(object sender, System.Windows.Controls.DataGridRowEventArgs e)
        {
            // 动态设置行高以适应内容
            e.Row.Height = double.NaN; // 设置为 NaN 可以让行高自动调整
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("确定要清除所有历史记录吗？", "确认", 
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                _historyService.ClearHistory();
                LoadHistory();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void HistoryDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (HistoryDataGrid.SelectedItem is BenchmarkResult result && !string.IsNullOrEmpty(result.Command))
            {
                Clipboard.SetText(result.Command);
                MessageBox.Show("命令已复制到剪贴板", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}


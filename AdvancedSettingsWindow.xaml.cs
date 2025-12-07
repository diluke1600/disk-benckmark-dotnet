using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DiskBenchmark.Models;
using DiskBenchmark.Services;

namespace DiskBenchmark
{
    public partial class AdvancedSettingsWindow : Window
    {
        public BenchmarkConfig Config { get; private set; }

        public AdvancedSettingsWindow(BenchmarkConfig config)
        {
            InitializeComponent();
            Config = config;
            LoadSettings();
        }

        private void LoadSettings()
        {
            UseFIOCheckBox.IsChecked = Config.UseFIO;
            FioPathTextBox.Text = Config.FioPath;
            UseDirectIOCheckBox.IsChecked = Config.UseDirectIO;
            TimeBasedCheckBox.IsChecked = Config.TimeBased;
            UseThreadCheckBox.IsChecked = Config.UseThread;
            
            // 显示FIO版本
            UpdateFioVersion();
            
            // 初始化块大小下拉框
            var blockSizes = new[] { 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096 };
            BlockSizeComboBox.ItemsSource = blockSizes;
            BlockSizeComboBox.SelectedItem = Config.BlockSizeKB;
            if (BlockSizeComboBox.SelectedItem == null && blockSizes.Length > 0)
            {
                var selected = blockSizes.FirstOrDefault(bs => bs >= Config.BlockSizeKB);
                BlockSizeComboBox.SelectedItem = selected != 0 ? selected : blockSizes[0];
            }
            
            // 应用范围限制并显示
            var threadCount = Math.Max(1, Math.Min(128, Config.ThreadCount));
            var queueDepth = Math.Max(1, Math.Min(256, Config.QueueDepth));
            ThreadCountTextBox.Text = threadCount.ToString();
            QueueDepthTextBox.Text = queueDepth.ToString();
            Config.ThreadCount = threadCount;
            Config.QueueDepth = queueDepth;
            
            // 设置IO引擎
            foreach (System.Windows.Controls.ComboBoxItem item in IOEngineComboBox.Items)
            {
                if (item.Content.ToString() == Config.IOEngine)
                {
                    IOEngineComboBox.SelectedItem = item;
                    break;
                }
            }
            if (IOEngineComboBox.SelectedItem == null && IOEngineComboBox.Items.Count > 0)
            {
                IOEngineComboBox.SelectedIndex = 0;
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            // 保存设置
            Config.UseFIO = UseFIOCheckBox.IsChecked ?? false;
            Config.FioPath = FioPathTextBox.Text;
            Config.UseDirectIO = UseDirectIOCheckBox.IsChecked ?? false;
            Config.TimeBased = TimeBasedCheckBox.IsChecked ?? false;
            Config.UseThread = UseThreadCheckBox.IsChecked ?? false;

            if (IOEngineComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
            {
                Config.IOEngine = selectedItem.Content.ToString() ?? "windowsaio";
            }

            if (BlockSizeComboBox.SelectedItem != null && int.TryParse(BlockSizeComboBox.SelectedItem.ToString(), out int blockSize))
            {
                Config.BlockSizeKB = blockSize;
            }

            if (int.TryParse(ThreadCountTextBox.Text, out int threadCount))
            {
                // 限制线程数范围：1-128
                Config.ThreadCount = Math.Max(1, Math.Min(128, threadCount));
                if (Config.ThreadCount != threadCount)
                {
                    ThreadCountTextBox.Text = Config.ThreadCount.ToString();
                }
            }

            if (int.TryParse(QueueDepthTextBox.Text, out int queueDepth))
            {
                // 限制队列深度范围：1-256
                Config.QueueDepth = Math.Max(1, Math.Min(256, queueDepth));
                if (Config.QueueDepth != queueDepth)
                {
                    QueueDepthTextBox.Text = Config.QueueDepth.ToString();
                }
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BrowseFioPath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
                Title = "选择FIO可执行文件",
                FileName = FioPathTextBox.Text
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                FioPathTextBox.Text = dialog.FileName;
                UpdateFioVersion();
            }
        }

        private void FioPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateFioVersion();
        }

        private void UpdateFioVersion()
        {
            try
            {
                var benchmarkService = new Services.BenchmarkService();
                var version = benchmarkService.GetFioVersion(FioPathTextBox.Text);
                FioVersionText.Text = $"FIO版本: {version}";
            }
            catch
            {
                FioVersionText.Text = "FIO版本: --";
            }
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            // 只允许输入数字
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void ThreadCountTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // 确保输入是正整数，范围在1-128
            if (sender is TextBox textBox)
            {
                if (string.IsNullOrWhiteSpace(textBox.Text) || !int.TryParse(textBox.Text, out int val))
                {
                    val = Math.Max(1, Math.Min(128, Config.ThreadCount));
                }
                else
                {
                    val = Math.Max(1, Math.Min(128, val));
                }
                Config.ThreadCount = val;
                textBox.Text = val.ToString();
            }
        }

        private void QueueDepthTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // 确保输入是正整数，范围在1-256
            if (sender is TextBox textBox)
            {
                if (string.IsNullOrWhiteSpace(textBox.Text) || !int.TryParse(textBox.Text, out int val))
                {
                    val = Math.Max(1, Math.Min(256, Config.QueueDepth));
                }
                else
                {
                    val = Math.Max(1, Math.Min(256, val));
                }
                Config.QueueDepth = val;
                textBox.Text = val.ToString();
            }
        }
    }
}


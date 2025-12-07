#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DiskBenchmark.Models;
using DiskBenchmark.Services;

namespace DiskBenchmark
{
    public partial class MainWindow : Window
    {
        private readonly BenchmarkService _benchmarkService;
        private readonly HistoryService _historyService;
        private BenchmarkConfig _config;
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isTestRunning = false;
        private System.Windows.Threading.DispatcherTimer? _countdownTimer;
        private int _remainingSeconds = 0;

        public MainWindow()
        {
            InitializeComponent();
            
            LogService.Info("主窗口初始化");
            _benchmarkService = new BenchmarkService();
            _historyService = new HistoryService();
            _config = new BenchmarkConfig();

            _benchmarkService.ProgressUpdated += OnProgressUpdated;
            _benchmarkService.TestCompleted += OnTestCompleted;

            InitializeUI();
            UpdateCommandPreview();
            UpdateFioVersion();
            
            // 确保在窗口完全加载后再次设置默认值
            this.Loaded += MainWindow_Loaded;
            
            LogService.Info("主窗口初始化完成");
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 确保测试类型默认值正确设置
            if (TestTypeComboBox.SelectedItem == null || (TestType)TestTypeComboBox.SelectedItem != _config.TestType)
            {
                var testTypes = Enum.GetValues(typeof(TestType));
                foreach (TestType testType in testTypes)
                {
                    if (testType == _config.TestType)
                    {
                        TestTypeComboBox.SelectedItem = testType;
                        LogService.Debug($"窗口加载后设置测试类型: {testType}");
                        break;
                    }
                }
                
                // 如果仍然没有选中项，强制设置为默认值
                if (TestTypeComboBox.SelectedItem == null)
                {
                    TestTypeComboBox.SelectedItem = TestType.RandomMixed;
                    _config.TestType = TestType.RandomMixed;
                    LogService.Debug("窗口加载后强制设置测试类型为随机混合");
                }
            }
        }

        private void InitializeUI()
        {
            // 初始化测试类型下拉框
            var testTypes = Enum.GetValues(typeof(TestType));
            TestTypeComboBox.ItemsSource = testTypes;
            
            // 确保默认值正确设置：找到匹配的枚举值并设置
            foreach (TestType testType in testTypes)
            {
                if (testType == _config.TestType)
                {
                    TestTypeComboBox.SelectedItem = testType;
                    break;
                }
            }
            
            // 如果仍然没有选中项，强制设置为默认值
            if (TestTypeComboBox.SelectedItem == null)
            {
                TestTypeComboBox.SelectedItem = TestType.RandomMixed;
                _config.TestType = TestType.RandomMixed;
            }

            // 初始化文件大小下拉框（带单位显示）
            var fileSizeOptions = new[]
            {
                new { Value = 100, Display = "100 MB" },
                new { Value = 200, Display = "200 MB" },
                new { Value = 500, Display = "500 MB" },
                new { Value = 1024, Display = "1 GB" },
                new { Value = 2048, Display = "2 GB" },
                new { Value = 5120, Display = "5 GB" },
                new { Value = 10240, Display = "10 GB" }
            };
            FileSizeComboBox.ItemsSource = fileSizeOptions;
            FileSizeComboBox.DisplayMemberPath = "Display";
            FileSizeComboBox.SelectedValuePath = "Value";
            FileSizeComboBox.SelectedValue = _config.FileSizeMB;
            if (FileSizeComboBox.SelectedValue == null)
            {
                FileSizeComboBox.SelectedValue = 1024; // 默认1GB
            }

            // 绑定配置变更事件
            TestPathTextBox.TextChanged += (s, e) => { _config.TestPath = TestPathTextBox.Text; UpdateCommandPreview(); };
            TestPathTextBox.LostFocus += TestPathTextBox_LostFocus;
            TestTypeComboBox.SelectionChanged += (s, e) => { if (TestTypeComboBox.SelectedItem != null) _config.TestType = (TestType)TestTypeComboBox.SelectedItem; UpdateCommandPreview(); };
            FileSizeComboBox.SelectionChanged += (s, e) => 
            { 
                if (FileSizeComboBox.SelectedValue != null && int.TryParse(FileSizeComboBox.SelectedValue.ToString(), out int val)) 
                { 
                    _config.FileSizeMB = val; 
                    UpdateCommandPreview(); 
                } 
            };
            DurationTextBox.TextChanged += (s, e) => 
            { 
                if (int.TryParse(DurationTextBox.Text, out int val)) 
                { 
                    // 限制持续时间范围：1-3600秒
                    val = Math.Max(1, Math.Min(3600, val));
                    _config.DurationSeconds = val;
                    // 如果值被限制，更新显示
                    if (val.ToString() != DurationTextBox.Text)
                    {
                        DurationTextBox.Text = val.ToString();
                    }
                    UpdateCommandPreview(); 
                } 
            };

            // 设置默认值，并应用范围限制
            TestPathTextBox.Text = _config.TestPath;
            var duration = Math.Max(1, Math.Min(3600, _config.DurationSeconds));
            _config.DurationSeconds = duration;
            DurationTextBox.Text = duration.ToString();
        }

        private void UpdateCommandPreview()
        {
            CommandPreviewTextBox.Text = _benchmarkService.GenerateCommandPreview(_config);
        }

        private void BrowsePath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _config.TestPath = dialog.SelectedPath;
                TestPathTextBox.Text = _config.TestPath;
                UpdateCommandPreview();
            }
        }

        private void TestPathTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // 验证路径 - 直接验证 TextBox 的实际内容，而不是 _config.TestPath
            // 这样可以确保验证的是用户实际输入的值，避免同步问题
            var actualPath = TestPathTextBox.Text;
            var pathValidationError = _benchmarkService.ValidateTestPath(actualPath);
            if (pathValidationError != null)
            {
                // 显示警告提示，但不阻止用户继续输入
                TestPathTextBox.ToolTip = $"路径验证失败: {pathValidationError}";
                TestPathTextBox.Background = System.Windows.Media.Brushes.LightPink;
                LogService.Warn($"测试路径验证失败: {pathValidationError}");
            }
            else
            {
                // 验证通过，更新配置并清除警告提示
                _config.TestPath = actualPath;
                TestPathTextBox.ToolTip = null;
                TestPathTextBox.Background = System.Windows.Media.Brushes.White;
            }
        }

        private void AdvancedSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new AdvancedSettingsWindow(_config);
            if (settingsWindow.ShowDialog() == true)
            {
                // 设置已更新，刷新命令预览和版本信息
                UpdateCommandPreview();
                UpdateFioVersion();
            }
        }

        private void FioParametersHelp_Click(object sender, RoutedEventArgs e)
        {
            var helpWindow = new FioParametersHelpWindow();
            helpWindow.ShowDialog();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? version?.ToString() ?? "0.0.1";
                
                // 从 InformationalVersion 中提取版本号和构建日期
                // 格式应该是: 0.0.1.20251207
                string versionText = "0.0.1";
                string buildDate = "";
                
                if (!string.IsNullOrEmpty(informationalVersion))
                {
                    var parts = informationalVersion.Split('.');
                    if (parts.Length >= 3)
                    {
                        versionText = $"{parts[0]}.{parts[1]}.{parts[2]}";
                        if (parts.Length >= 4 && parts[3].Length == 8)
                        {
                            // 解析日期: yyyyMMdd
                            if (DateTime.TryParseExact(parts[3], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime date))
                            {
                                buildDate = date.ToString("yyyy-MM-dd");
                            }
                            else
                            {
                                buildDate = parts[3];
                            }
                        }
                    }
                    else
                    {
                        versionText = informationalVersion;
                    }
                }
                
                string message = "磁盘性能测试工具\n\n" +
                                $"版本: {versionText}\n";
                
                if (!string.IsNullOrEmpty(buildDate))
                {
                    message += $"构建日期: {buildDate}\n";
                }
                
                message += "\n支持自定义测试和FIO测试\n\n" +
                          "使用菜单中的\"高级设置\"可以配置更多参数。";
                
                MessageBox.Show(
                    message,
                    "关于",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogService.Error($"显示关于对话框失败: {ex.Message}", ex);
                MessageBox.Show(
                    "磁盘性能测试工具\n\n" +
                    "版本: 0.0.1\n" +
                    "支持自定义测试和FIO测试\n\n" +
                    "使用菜单中的\"高级设置\"可以配置更多参数。",
                    "关于",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private async void StartTest_Click(object sender, RoutedEventArgs e)
        {
            if (_isTestRunning) return;

            LogService.Info("用户点击开始测试按钮");
            
            // 验证测试路径
            var pathValidationError = _benchmarkService.ValidateTestPath(_config.TestPath);
            if (pathValidationError != null)
            {
                MessageBox.Show(
                    $"测试路径验证失败:\n\n{pathValidationError}\n\n请修正路径后重试。",
                    "路径验证失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                LogService.Warn($"测试路径验证失败: {pathValidationError}");
                return;
            }
            
            _isTestRunning = true;
            StartTestButton.IsEnabled = false;
            StopTestButton.IsEnabled = true;
            StatusText.Text = "测试中...";
            ProgressText.Text = "准备开始测试...";

            // 清空之前的结果
            ReadSpeedText.Text = "-- MB/s";
            WriteSpeedText.Text = "-- MB/s";
            ReadIOPSText.Text = "--";
            WriteIOPSText.Text = "--";
            ReadLatencyText.Text = "-- ms";
            WriteLatencyText.Text = "-- ms";
            
            // 清空详细报告
            DetailTestTimeText.Text = "--";
            DetailTestPathText.Text = "--";
            DetailTestTypeText.Text = "--";
            DetailFileSizeText.Text = "--";
            DetailBlockSizeText.Text = "--";
            DetailThreadCountText.Text = "--";
            DetailQueueDepthText.Text = "--";
            DetailDurationText.Text = "--";
            DetailFioVersionText.Text = "--";
            DetailCommandText.Text = "--";
            DetailErrorMessageText.Text = "--";
            DetailErrorMessageText.Foreground = System.Windows.Media.Brushes.Gray;

            _cancellationTokenSource = new CancellationTokenSource();

            // 如果是 time_based 模式，启动倒计时
            if (_config.TimeBased && _config.DurationSeconds > 0)
            {
                _remainingSeconds = _config.DurationSeconds;
                StartCountdown();
                LogService.Debug($"启动倒计时，持续时间: {_config.DurationSeconds} 秒");
            }

            try
            {
                var result = await _benchmarkService.RunBenchmarkAsync(_config);
                if (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    DisplayResult(result);
                    _historyService.AddResult(result);
                    LogService.Info($"测试完成并已保存到历史记录");
                }
                else
                {
                    LogService.Info("测试被用户取消");
                    StatusText.Text = "已取消";
                    ProgressText.Text = "测试已被用户取消";
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"测试执行异常: {ex.Message}", ex);
                StatusText.Text = $"错误: {ex.Message}";
                ProgressText.Text = $"错误: {ex.Message}";
                ProgressText.Foreground = System.Windows.Media.Brushes.Red;
            }
            finally
            {
                StopCountdown();
                _isTestRunning = false;
                StartTestButton.IsEnabled = true;
                StopTestButton.IsEnabled = false;
                StartTestButtonText.Text = "开始测试";
                // 确保状态正确更新
                if (StatusText.Text == "测试中...")
                {
                    StatusText.Text = "完成";
                }
            }
        }

        private void StartCountdown()
        {
            if (_countdownTimer != null)
            {
                _countdownTimer.Stop();
                _countdownTimer = null;
            }

            _countdownTimer = new System.Windows.Threading.DispatcherTimer();
            _countdownTimer.Interval = TimeSpan.FromSeconds(1);
            _countdownTimer.Tick += CountdownTimer_Tick;
            _countdownTimer.Start();
            UpdateCountdownDisplay();
        }

        private void StopCountdown()
        {
            if (_countdownTimer != null)
            {
                _countdownTimer.Stop();
                _countdownTimer = null;
            }
        }

        private void CountdownTimer_Tick(object? sender, EventArgs e)
        {
            if (_remainingSeconds > 0)
            {
                _remainingSeconds--;
                UpdateCountdownDisplay();
            }
            else
            {
                StopCountdown();
            }
        }

        private void UpdateCountdownDisplay()
        {
            if (_remainingSeconds > 0)
            {
                var minutes = _remainingSeconds / 60;
                var seconds = _remainingSeconds % 60;
                StartTestButtonText.Text = $"测试中 ({minutes:D2}:{seconds:D2})";
            }
            else
            {
                StartTestButtonText.Text = "开始测试";
            }
        }

        private void StopTest_Click(object sender, RoutedEventArgs e)
        {
            LogService.Info("用户点击停止测试按钮");
            _cancellationTokenSource?.Cancel();
            StopCountdown();
            StatusText.Text = "已取消";
            ProgressText.Text = "";
            _isTestRunning = false;
            StartTestButton.IsEnabled = true;
            StopTestButton.IsEnabled = false;
            StartTestButtonText.Text = "开始测试";
        }

        private void DisplayResult(BenchmarkResult result)
        {
            if (result == null) return;
            
            // 基本测试结果
            ReadSpeedText.Text = result.ReadSpeed > 0 ? $"{result.ReadSpeed:F2} MB/s" : "-- MB/s";
            WriteSpeedText.Text = result.WriteSpeed > 0 ? $"{result.WriteSpeed:F2} MB/s" : "-- MB/s";
            ReadIOPSText.Text = result.ReadIOPS > 0 ? $"{result.ReadIOPS:F0}" : "--";
            WriteIOPSText.Text = result.WriteIOPS > 0 ? $"{result.WriteIOPS:F0}" : "--";
            ReadLatencyText.Text = result.ReadLatency > 0 ? $"{result.ReadLatency:F2} ms" : "-- ms";
            WriteLatencyText.Text = result.WriteLatency > 0 ? $"{result.WriteLatency:F2} ms" : "-- ms";
            StatusText.Text = string.IsNullOrEmpty(result.Status) ? "完成" : result.Status;
            
            // 详细报告
            DetailTestTimeText.Text = result.TestTime.ToString("yyyy-MM-dd HH:mm:ss");
            DetailTestPathText.Text = string.IsNullOrEmpty(result.TestPath) ? "--" : result.TestPath;
            DetailTestTypeText.Text = GetTestTypeDisplayName(result.TestType);
            
            // 文件大小显示（转换为GB/MB）
            if (result.Config != null && result.Config.FileSizeMB > 0)
            {
                if (result.Config.FileSizeMB >= 1024)
                {
                    double gb = result.Config.FileSizeMB / 1024.0;
                    DetailFileSizeText.Text = $"{gb:F2} GB ({result.Config.FileSizeMB} MB)";
                }
                else
                {
                    DetailFileSizeText.Text = $"{result.Config.FileSizeMB} MB";
                }
            }
            else
            {
                DetailFileSizeText.Text = "--";
            }
            
            DetailBlockSizeText.Text = result.BlockSizeKB > 0 ? $"{result.BlockSizeKB} KB" : "--";
            DetailThreadCountText.Text = result.ThreadCount > 0 ? result.ThreadCount.ToString() : "--";
            DetailQueueDepthText.Text = result.QueueDepth > 0 ? result.QueueDepth.ToString() : "--";
            
            if (result.Config != null && result.Config.DurationSeconds > 0)
            {
                DetailDurationText.Text = $"{result.Config.DurationSeconds} 秒";
            }
            else
            {
                DetailDurationText.Text = "--";
            }
            
            DetailFioVersionText.Text = string.IsNullOrEmpty(result.FioVersion) ? "--" : result.FioVersion;
            DetailCommandText.Text = string.IsNullOrEmpty(result.Command) ? "--" : result.Command;
            DetailErrorMessageText.Text = string.IsNullOrEmpty(result.ErrorMessage) ? "--" : result.ErrorMessage;
            
            // 如果有错误信息，显示为红色，否则隐藏
            if (string.IsNullOrEmpty(result.ErrorMessage))
            {
                DetailErrorMessageText.Foreground = System.Windows.Media.Brushes.Gray;
            }
            else
            {
                DetailErrorMessageText.Foreground = System.Windows.Media.Brushes.Red;
            }
            
            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                ProgressText.Text = $"错误: {result.ErrorMessage}";
                ProgressText.Foreground = System.Windows.Media.Brushes.Red;
            }
            else
            {
                ProgressText.Text = "测试完成";
                ProgressText.Foreground = System.Windows.Media.Brushes.Green;
            }
        }

        private string GetTestTypeDisplayName(TestType testType)
        {
            return testType switch
            {
                TestType.SequentialRead => "顺序读取",
                TestType.SequentialWrite => "顺序写入",
                TestType.RandomRead => "随机读取",
                TestType.RandomWrite => "随机写入",
                TestType.Mixed => "混合测试",
                TestType.RandomMixed => "随机混合",
                _ => testType.ToString()
            };
        }

        private void OnProgressUpdated(string message)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressText.Text = message;
            });
        }

        private void OnTestCompleted(BenchmarkResult result)
        {
            Dispatcher.Invoke(() =>
            {
                DisplayResult(result);
                // 确保状态同步
                StopCountdown();
                _isTestRunning = false;
                StartTestButton.IsEnabled = true;
                StopTestButton.IsEnabled = false;
                StartTestButtonText.Text = "开始测试";
            });
        }

        private void ViewHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogService.Debug("用户打开历史记录窗口");
                var historyWindow = new HistoryWindow(_historyService);
                historyWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                LogService.Error($"打开历史记录窗口失败: {ex.Message}", ex);
                MessageBox.Show($"打开历史记录窗口失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetConfig_Click(object sender, RoutedEventArgs e)
        {
            LogService.Info("用户重置配置");
            _config = new BenchmarkConfig();
            InitializeUI();
            UpdateCommandPreview();
            UpdateFioVersion();
        }

        private void UpdateFioVersion()
        {
            try
            {
                if (_config.UseFIO && !string.IsNullOrEmpty(_config.FioPath))
                {
                    LogService.Debug($"开始获取FIO版本，路径: {_config.FioPath}");
                    var version = _benchmarkService.GetFioVersion(_config.FioPath);
                    var actualPath = _benchmarkService.GetFioActualPath(_config.FioPath);
                    LogService.Debug($"获取到FIO版本: {version}, 实际路径: {actualPath}");
                    
                    if (!string.IsNullOrEmpty(actualPath) && File.Exists(actualPath))
                    {
                        FioVersionText.Text = $"FIO版本: {version} | 位置: {actualPath}";
                    }
                    else
                    {
                        FioVersionText.Text = $"FIO版本: {version}";
                    }
                }
                else
                {
                    FioVersionText.Text = "";
                }
            }
            catch (Exception ex)
            {
                LogService.Warn($"更新FIO版本显示失败: {ex.Message}");
                FioVersionText.Text = "";
            }
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            // 只允许输入数字
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void CommandPreviewTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            CopyCommandToClipboard();
        }

        private void CopyCommand_Click(object sender, RoutedEventArgs e)
        {
            CopyCommandToClipboard();
        }

        private void CopyCommandToClipboard()
        {
            if (!string.IsNullOrEmpty(CommandPreviewTextBox.Text))
            {
                Clipboard.SetText(CommandPreviewTextBox.Text);
                LogService.Debug("命令已复制到剪贴板");
                ProgressText.Text = "命令已复制到剪贴板";
                ProgressText.Foreground = System.Windows.Media.Brushes.Green;
            }
        }

        private void DurationTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // 确保输入是正整数，范围在1-3600
            if (sender is TextBox textBox)
            {
                if (string.IsNullOrWhiteSpace(textBox.Text) || !int.TryParse(textBox.Text, out int val))
                {
                    val = Math.Max(1, Math.Min(3600, _config.DurationSeconds));
                }
                else
                {
                    val = Math.Max(1, Math.Min(3600, val));
                }
                _config.DurationSeconds = val;
                textBox.Text = val.ToString();
                UpdateCommandPreview();
            }
        }
    }

    public class InverseBooleanConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false;
        }
    }
}
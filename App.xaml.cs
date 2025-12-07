using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using DiskBenchmark.Services;
using NLog;

namespace DiskBenchmark
{
    public partial class App : Application
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // 检查操作系统版本（支持 Windows Server 2016 及更高版本）
            CheckOSVersion();
            
            // 确保日志目录存在
            var logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            // 配置 NLog（使用配置文件）
            var configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NLog.config");
            if (File.Exists(configFile))
            {
                try
                {
                    var config = new NLog.Config.XmlLoggingConfiguration(configFile);
                    LogManager.Configuration = config;
                }
                catch (Exception ex)
                {
                    LogService.Warn($"加载NLog配置文件失败: {ex.Message}");
                    // 如果加载失败，使用代码配置
                    SetupNLogProgrammatically(logDirectory);
                }
            }
            else
            {
                // 如果配置文件不存在，使用代码配置
                SetupNLogProgrammatically(logDirectory);
            }

            Logger.Info("应用程序启动");
            LogService.Info("磁盘性能测试工具启动");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            LogService.Info("应用程序退出");
            LogManager.Shutdown();
            base.OnExit(e);
        }

        private void CheckOSVersion()
        {
            try
            {
                var osVersion = Environment.OSVersion;
                var version = osVersion.Version;
                LogService.Info($"操作系统版本: {osVersion} (Windows {version.Major}.{version.Minor}.{version.Build})");
            }
            catch (Exception ex)
            {
                LogService.Warn($"检查操作系统版本失败: {ex.Message}");
            }
        }

        private void SetupNLogProgrammatically(string logDirectory)
        {
            // 如果配置文件不存在，使用代码配置
            var config = new NLog.Config.LoggingConfiguration();
            var logfile = new NLog.Targets.FileTarget("logfile")
            {
                FileName = Path.Combine(logDirectory, "disk-benchmark-${shortdate}.log"),
                Layout = "${longdate} [${level:uppercase=true}] ${logger} - ${message} ${exception:format=tostring}",
                ArchiveFileName = Path.Combine(logDirectory, "archive", "disk-benchmark-${shortdate}.log"),
                ArchiveEvery = NLog.Targets.FileArchivePeriod.Day,
                ArchiveNumbering = NLog.Targets.ArchiveNumberingMode.Rolling,
                MaxArchiveFiles = 30
            };
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, logfile);
            LogManager.Configuration = config;
        }
    }
}


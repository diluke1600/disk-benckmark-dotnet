using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiskBenchmark.Models;
using Newtonsoft.Json;

namespace DiskBenchmark.Services
{
    public class HistoryService
    {
        private readonly string _historyFilePath;
        private List<BenchmarkResult> _history;

        public HistoryService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DiskBenchmark");
            
            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
                LogService.Debug($"创建历史记录目录: {appDataPath}");
            }

            _historyFilePath = Path.Combine(appDataPath, "history.json");
            _history = LoadHistory();
            LogService.Info($"历史记录服务初始化完成，已加载 {_history.Count} 条记录");
        }

        public List<BenchmarkResult> GetHistory()
        {
            var history = _history.OrderByDescending(r => r.TestTime).ToList();
            LogService.Debug($"获取历史记录，共 {history.Count} 条");
            return history;
        }

        public void AddResult(BenchmarkResult result)
        {
            _history.Add(result);
            SaveHistory();
            LogService.Info($"添加测试结果到历史记录 - 时间: {result.TestTime}, 类型: {result.TestType}, 状态: {result.Status}");
        }

        public void ClearHistory()
        {
            var count = _history.Count;
            _history.Clear();
            SaveHistory();
            LogService.Info($"清除历史记录，共清除 {count} 条记录");
        }

        private List<BenchmarkResult> LoadHistory()
        {
            if (File.Exists(_historyFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_historyFilePath);
                    var history = JsonConvert.DeserializeObject<List<BenchmarkResult>>(json) ?? new List<BenchmarkResult>();
                    LogService.Debug($"从文件加载历史记录: {_historyFilePath}，共 {history.Count} 条");
                    return history;
                }
                catch (Exception ex)
                {
                    LogService.Error($"加载历史记录失败: {ex.Message}", ex);
                    return new List<BenchmarkResult>();
                }
            }
            LogService.Debug("历史记录文件不存在，创建新的历史记录列表");
            return new List<BenchmarkResult>();
        }

        private void SaveHistory()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_history, Formatting.Indented);
                File.WriteAllText(_historyFilePath, json);
                LogService.Debug($"保存历史记录到文件: {_historyFilePath}，共 {_history.Count} 条");
            }
            catch (Exception ex)
            {
                LogService.Error($"保存历史记录失败: {ex.Message}", ex);
            }
        }
    }
}


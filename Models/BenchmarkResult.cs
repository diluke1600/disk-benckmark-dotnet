using System;

namespace DiskBenchmark.Models
{
    public class BenchmarkResult
    {
        public DateTime TestTime { get; set; } = DateTime.Now;
        public string TestPath { get; set; } = "";
        public TestType TestType { get; set; }
        
        // 性能指标 (MB/s)
        public double ReadSpeed { get; set; }
        public double WriteSpeed { get; set; }
        
        // IOPS
        public double ReadIOPS { get; set; }
        public double WriteIOPS { get; set; }
        
        // 延迟 (ms)
        public double ReadLatency { get; set; }
        public double WriteLatency { get; set; }
        
        // 配置信息
        public BenchmarkConfig Config { get; set; } = new BenchmarkConfig();
        
        // 测试状态
        public string Status { get; set; } = "完成";
        public string ErrorMessage { get; set; } = "";
        
        // 测试命令
        public string Command { get; set; } = "";
        
        // FIO版本
        public string FioVersion { get; set; } = "";
        
        // 测试参数
        public int ThreadCount { get; set; }
        public int QueueDepth { get; set; }
        public int BlockSizeKB { get; set; }
    }
}


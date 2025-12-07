using System;
using System.IO;

namespace DiskBenchmark.Models
{
    public class BenchmarkConfig
    {
        // 测试路径 - 默认为程序当前路径
        public string TestPath { get; set; } = AppDomain.CurrentDomain.BaseDirectory;

        // 测试文件大小 (MB)
        public int FileSizeMB { get; set; } = 1024; // 默认1GB

        // 块大小 (KB)
        public int BlockSizeKB { get; set; } = 512;

        // 测试类型
        public TestType TestType { get; set; } = TestType.RandomMixed;

        // 测试线程数
        public int ThreadCount { get; set; } = 16;

        // 测试队列深度
        public int QueueDepth { get; set; } = 16;

        // 测试持续时间 (秒)
        public int DurationSeconds { get; set; } = 30;

        // 是否使用FIO
        public bool UseFIO { get; set; } = true;

        // FIO可执行文件路径
        public string FioPath { get; set; } = "fio.exe";

        // 是否使用直接IO（绕过缓存）
        public bool UseDirectIO { get; set; } = true;

        // 是否使用基于时间的测试（time_based）
        public bool TimeBased { get; set; } = true;

        // IO引擎类型
        public string IOEngine { get; set; } = "windowsaio";

        // 是否使用thread参数
        public bool UseThread { get; set; } = true;
        
        // FIO版本
        public string FioVersion { get; set; } = "";
    }

    public enum TestType
    {
        SequentialRead,    // 顺序读取
        SequentialWrite,   // 顺序写入
        RandomRead,        // 随机读取
        RandomWrite,       // 随机写入
        Mixed,             // 混合测试
        RandomMixed        // 随机混合
    }
}


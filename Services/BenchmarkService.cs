#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiskBenchmark.Models;
using Newtonsoft.Json.Linq;

namespace DiskBenchmark.Services
{
    public class BenchmarkService
    {
        public event Action<string>? ProgressUpdated;
        public event Action<BenchmarkResult>? TestCompleted;

        /// <summary>
        /// 获取FIO实际文件路径（如果只是文件名，会在PATH中查找）
        /// </summary>
        public string GetFioActualPath(string fioPath)
        {
            if (string.IsNullOrEmpty(fioPath))
            {
                return "";
            }

            // 如果已经是绝对路径且文件存在，直接返回
            if (Path.IsPathRooted(fioPath) && File.Exists(fioPath))
            {
                return Path.GetFullPath(fioPath);
            }

            // 如果是相对路径且文件存在，返回完整路径
            if (File.Exists(fioPath))
            {
                return Path.GetFullPath(fioPath);
            }

            // 如果只是文件名（如 "fio.exe"），尝试在 PATH 中查找
            if (!Path.IsPathRooted(fioPath))
            {
                var pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(pathEnv))
                {
                    var paths = pathEnv.Split(Path.PathSeparator);
                    foreach (var path in paths)
                    {
                        var fullPath = Path.Combine(path, fioPath);
                        if (File.Exists(fullPath))
                        {
                            return Path.GetFullPath(fullPath);
                        }
                    }
                }
            }

            return fioPath; // 如果找不到，返回原始路径
        }

        /// <summary>
        /// 获取FIO版本信息
        /// </summary>
        public string GetFioVersion(string fioPath)
        {
            try
            {
                if (string.IsNullOrEmpty(fioPath))
                {
                    LogService.Debug("FIO路径为空");
                    return "未配置";
                }

                // 获取实际文件路径
                string actualPath = GetFioActualPath(fioPath);

                if (string.IsNullOrEmpty(actualPath) || !File.Exists(actualPath))
                {
                    LogService.Debug($"FIO文件不存在: {actualPath}");
                    return "未找到";
                }

                var processInfo = new ProcessStartInfo
                {
                    FileName = actualPath,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(actualPath) ?? Environment.CurrentDirectory
                };

                using var process = Process.Start(processInfo);
                if (process != null)
                {
                    // 先等待进程退出，再读取输出
                    process.WaitForExit(5000); // 最多等待5秒
                    
                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();

                    LogService.Debug($"FIO版本命令输出: {output}");
                    LogService.Debug($"FIO版本命令错误: {error}");

                    // 合并标准输出和错误输出
                    var allOutput = output + "\n" + error;
                    
                    if (!string.IsNullOrEmpty(allOutput))
                    {
                        var lines = allOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            var trimmedLine = line.Trim();
                            if (string.IsNullOrEmpty(trimmedLine))
                                continue;

                            // 尝试多种版本格式匹配
                            // 格式1: "fio-3.35"
                            var match1 = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"fio[-\s]+([\d.]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (match1.Success)
                            {
                                var version = $"fio-{match1.Groups[1].Value}";
                                LogService.Debug($"匹配到版本格式1: {version}");
                                return version;
                            }

                            // 格式2: "fio version 3.35"
                            var match2 = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"fio\s+version\s+([\d.]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (match2.Success)
                            {
                                var version = $"fio-{match2.Groups[1].Value}";
                                LogService.Debug($"匹配到版本格式2: {version}");
                                return version;
                            }

                            // 格式3: 包含版本号的任何行
                            var match3 = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"([\d]+\.[\d]+(?:\.[\d]+)?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (match3.Success && trimmedLine.ToLower().Contains("fio"))
                            {
                                var version = $"fio-{match3.Groups[1].Value}";
                                LogService.Debug($"匹配到版本格式3: {version}");
                                return version;
                            }

                            // 如果行中包含 fio 和 version，返回整行
                            if (trimmedLine.ToLower().Contains("fio") && trimmedLine.ToLower().Contains("version"))
                            {
                                LogService.Debug($"返回包含版本信息的行: {trimmedLine}");
                                return trimmedLine;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Warn($"获取FIO版本失败: {ex.Message}");
                LogService.Debug($"异常详情: {ex}");
            }

            return "未知";
        }

        public async Task<BenchmarkResult> RunBenchmarkAsync(BenchmarkConfig config)
        {
            LogService.Info($"开始执行测试 - 类型: {config.TestType}, 路径: {config.TestPath}, 使用FIO: {config.UseFIO}");
            
            // 获取FIO版本（如果使用FIO）
            string fioVersion = "";
            if (config.UseFIO)
            {
                fioVersion = GetFioVersion(config.FioPath);
                LogService.Info($"检测到FIO版本: {fioVersion}");
            }
            
            // 创建配置的深拷贝，确保历史记录中的配置不会被后续修改影响
            var configCopy = new BenchmarkConfig
            {
                TestPath = config.TestPath,
                FileSizeMB = config.FileSizeMB,
                BlockSizeKB = config.BlockSizeKB,
                TestType = config.TestType,
                ThreadCount = config.ThreadCount,
                QueueDepth = config.QueueDepth,
                DurationSeconds = config.DurationSeconds,
                UseFIO = config.UseFIO,
                FioPath = config.FioPath,
                UseDirectIO = config.UseDirectIO,
                TimeBased = config.TimeBased,
                IOEngine = config.IOEngine,
                UseThread = config.UseThread,
                FioVersion = fioVersion
            };
            
            // 生成测试命令
            var command = GenerateCommandPreview(configCopy);
            
            var result = new BenchmarkResult
            {
                TestTime = DateTime.Now,
                TestPath = config.TestPath,
                TestType = config.TestType,
                Config = configCopy,
                Command = command,
                FioVersion = fioVersion,
                ThreadCount = config.ThreadCount,
                QueueDepth = config.QueueDepth,
                BlockSizeKB = config.BlockSizeKB
            };

            try
            {
                if (config.UseFIO)
                {
                    LogService.Debug("使用FIO进行测试");
                    var fioResult = await RunFIOBenchmarkAsync(config);
                    fioResult.Command = command; // 确保命令被保存
                    return fioResult;
                }
                else
                {
                    LogService.Debug("使用自定义测试方法");
                    var customResult = await RunCustomBenchmarkAsync(config);
                    customResult.Command = command; // 确保命令被保存
                    return customResult;
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"测试执行失败: {ex.Message}", ex);
                result.Status = "失败";
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private async Task<BenchmarkResult> RunCustomBenchmarkAsync(BenchmarkConfig config)
        {
            LogService.Info($"开始自定义测试 - 文件大小: {config.FileSizeMB}MB, 块大小: {config.BlockSizeKB}KB, 线程数: {config.ThreadCount}, 队列深度: {config.QueueDepth}");
            
            // 获取FIO版本
            string fioVersion = "";
            if (config.UseFIO)
            {
                fioVersion = GetFioVersion(config.FioPath);
            }
            
            // 创建配置的深拷贝
            var configCopy = new BenchmarkConfig
            {
                TestPath = config.TestPath,
                FileSizeMB = config.FileSizeMB,
                BlockSizeKB = config.BlockSizeKB,
                TestType = config.TestType,
                ThreadCount = config.ThreadCount,
                QueueDepth = config.QueueDepth,
                DurationSeconds = config.DurationSeconds,
                UseFIO = config.UseFIO,
                FioPath = config.FioPath,
                UseDirectIO = config.UseDirectIO,
                TimeBased = config.TimeBased,
                IOEngine = config.IOEngine,
                UseThread = config.UseThread,
                FioVersion = fioVersion
            };
            
            var result = new BenchmarkResult
            {
                TestTime = DateTime.Now,
                TestPath = config.TestPath,
                TestType = config.TestType,
                Config = configCopy,
                Status = "进行中",
                Command = GenerateCommandPreview(configCopy),
                FioVersion = fioVersion,
                ThreadCount = config.ThreadCount,
                QueueDepth = config.QueueDepth,
                BlockSizeKB = config.BlockSizeKB
            };

            await Task.Run(() =>
            {
                try
                {
                    var testFile = Path.Combine(config.TestPath, "benchmark_test.tmp");
                    var fileSize = config.FileSizeMB * 1024 * 1024L;
                    var blockSize = config.BlockSizeKB * 1024;
                    var buffer = new byte[blockSize];

                    // 对于混合测试，先写入再读取
                    // 对于纯写入测试，只执行写入
                    // 对于纯读取测试，需要先创建文件
                    if (config.TestType == TestType.SequentialRead || config.TestType == TestType.RandomRead)
                    {
                        // 纯读取测试，先创建文件
                        ProgressUpdated?.Invoke("正在准备测试文件...");
                        using (var fs = new FileStream(testFile, FileMode.Create, FileAccess.Write))
                        {
                            var writeBuffer = new byte[blockSize];
                            for (long i = 0; i < fileSize; i += blockSize)
                            {
                                fs.Write(writeBuffer, 0, blockSize);
                            }
                        }
                    }

                    // 写入测试
                    if (config.TestType == TestType.SequentialWrite || 
                        config.TestType == TestType.RandomWrite ||
                        config.TestType == TestType.Mixed ||
                        config.TestType == TestType.RandomMixed)
                    {
                        LogService.Debug("开始写入测试");
                        ProgressUpdated?.Invoke("正在进行写入测试...");
                        var writeResult = PerformWriteTest(testFile, fileSize, blockSize, config);
                        result.WriteSpeed = writeResult.Speed;
                        result.WriteIOPS = writeResult.IOPS;
                        result.WriteLatency = writeResult.Latency;
                        LogService.Info($"写入测试完成 - 速度: {writeResult.Speed:F2} MB/s, IOPS: {writeResult.IOPS:F0}, 延迟: {writeResult.Latency:F2} ms");
                    }

                    // 读取测试
                    if (config.TestType == TestType.SequentialRead || 
                        config.TestType == TestType.RandomRead ||
                        config.TestType == TestType.Mixed ||
                        config.TestType == TestType.RandomMixed)
                    {
                        LogService.Debug("开始读取测试");
                        ProgressUpdated?.Invoke("正在进行读取测试...");
                        var readResult = PerformReadTest(testFile, fileSize, blockSize, config);
                        result.ReadSpeed = readResult.Speed;
                        result.ReadIOPS = readResult.IOPS;
                        result.ReadLatency = readResult.Latency;
                        LogService.Info($"读取测试完成 - 速度: {readResult.Speed:F2} MB/s, IOPS: {readResult.IOPS:F0}, 延迟: {readResult.Latency:F2} ms");
                    }

                    result.Status = "完成";
                    LogService.Info($"自定义测试完成 - 读取速度: {result.ReadSpeed:F2} MB/s, 写入速度: {result.WriteSpeed:F2} MB/s");

                    // 清理测试文件
                    if (File.Exists(testFile))
                    {
                        try
                        {
                            File.Delete(testFile);
                            LogService.Debug($"已删除测试文件: {testFile}");
                        }
                        catch (Exception ex)
                        {
                            LogService.Warn($"删除测试文件失败: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error($"自定义测试执行失败: {ex.Message}", ex);
                    result.Status = "失败";
                    result.ErrorMessage = ex.Message;
                }
            });

            // 确保测试完成后状态正确（如果仍然是"进行中"，则更新为"完成"）
            if (result.Status == "进行中")
            {
                result.Status = "完成";
            }

            TestCompleted?.Invoke(result);
            return result;
        }

        private (double Speed, double IOPS, double Latency) PerformWriteTest(
            string filePath, long fileSize, int blockSize, BenchmarkConfig config)
        {
            var random = new Random();
            var buffer = new byte[blockSize];
            random.NextBytes(buffer);

            var startTime = DateTime.Now;
            long totalBytes = 0;
            int operationCount = 0;

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, blockSize))
            {
                var endTime = startTime.AddSeconds(config.DurationSeconds);
                
                while (DateTime.Now < endTime && totalBytes < fileSize)
                {
                    if (config.TestType == TestType.RandomWrite || config.TestType == TestType.Mixed || config.TestType == TestType.RandomMixed)
                    {
                        fs.Seek(random.Next(0, (int)Math.Min(fileSize, fs.Length + blockSize)), SeekOrigin.Begin);
                    }
                    
                    fs.Write(buffer, 0, blockSize);
                    totalBytes += blockSize;
                    operationCount++;
                }
            }

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            if (elapsed <= 0 || operationCount == 0)
            {
                return (0, 0, 0);
            }
            var speed = totalBytes / 1024.0 / 1024.0 / elapsed; // MB/s
            var iops = operationCount / elapsed;
            var latency = elapsed / operationCount * 1000; // ms

            return (speed, iops, latency);
        }

        private (double Speed, double IOPS, double Latency) PerformReadTest(
            string filePath, long fileSize, int blockSize, BenchmarkConfig config)
        {
            if (!File.Exists(filePath))
            {
                // 如果文件不存在，先创建
                using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                {
                    var writeBuffer = new byte[blockSize];
                    for (long i = 0; i < fileSize; i += blockSize)
                    {
                        fs.Write(writeBuffer, 0, blockSize);
                    }
                }
            }

            var random = new Random();
            var buffer = new byte[blockSize];

            var startTime = DateTime.Now;
            long totalBytes = 0;
            int operationCount = 0;

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, blockSize))
            {
                var endTime = startTime.AddSeconds(config.DurationSeconds);
                
                while (DateTime.Now < endTime && totalBytes < fileSize)
                {
                    if (config.TestType == TestType.RandomRead || config.TestType == TestType.Mixed || config.TestType == TestType.RandomMixed)
                    {
                        var maxPos = Math.Max(0, fs.Length - blockSize);
                        fs.Seek(random.Next(0, (int)maxPos), SeekOrigin.Begin);
                    }
                    
                    var bytesRead = fs.Read(buffer, 0, blockSize);
                    if (bytesRead == 0) break;
                    
                    totalBytes += bytesRead;
                    operationCount++;
                }
            }

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            if (elapsed <= 0 || operationCount == 0)
            {
                return (0, 0, 0);
            }
            var speed = totalBytes / 1024.0 / 1024.0 / elapsed; // MB/s
            var iops = operationCount / elapsed;
            var latency = elapsed / operationCount * 1000; // ms

            return (speed, iops, latency);
        }

        private void ParseWinSATOutput(string output, BenchmarkResult result)
        {
            // 简单的WinSAT输出解析
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("Disk  Sequential"))
                {
                    // 解析顺序读写速度
                }
                else if (line.Contains("Disk  Random"))
                {
                    // 解析随机读写速度
                }
            }
        }

        public string GenerateCommandPreview(BenchmarkConfig config)
        {
            if (config.UseFIO)
            {
                return GenerateFIOCommand(config);
            }
            else
            {
                var testTypeStr = config.TestType switch
                {
                    TestType.SequentialRead => "顺序读取",
                    TestType.SequentialWrite => "顺序写入",
                    TestType.RandomRead => "随机读取",
                    TestType.RandomWrite => "随机写入",
                    TestType.Mixed => "混合测试",
                    TestType.RandomMixed => "随机混合",
                    _ => "未知"
                };

                return $"自定义测试: {testTypeStr}\n" +
                       $"  路径: {config.TestPath}\n" +
                       $"  文件大小: {config.FileSizeMB} MB\n" +
                       $"  块大小: {config.BlockSizeKB} KB\n" +
                       $"  线程数: {config.ThreadCount}\n" +
                       $"  队列深度: {config.QueueDepth}\n" +
                       $"  持续时间: {config.DurationSeconds} 秒";
            }
        }

        /// <summary>
        /// 将Windows路径转换为FIO需要的格式 (例如: C:\test\file.tmp -> c\:test\file.tmp)
        /// </summary>
        private string ConvertPathForFIO(string windowsPath)
        {
            if (string.IsNullOrEmpty(windowsPath))
            {
                return windowsPath;
            }

            // 如果是绝对路径（包含盘符），转换格式
            if (Path.IsPathRooted(windowsPath) && windowsPath.Length >= 2 && windowsPath[1] == ':')
            {
                // 提取盘符（转换为小写）和剩余路径
                var driveLetter = char.ToLower(windowsPath[0]);
                var remainingPath = windowsPath.Substring(2); // 跳过 "C:"
                
                // 将反斜杠转换为反斜杠（保持反斜杠，因为FIO在Windows上使用反斜杠）
                // 格式: c\:path\to\file
                return $"{driveLetter}\\:{remainingPath}";
            }

            // 如果不是绝对路径，直接返回
            return windowsPath;
        }

        private string GenerateFIOCommand(BenchmarkConfig config)
        {
            var testFile = Path.Combine(config.TestPath, "fio_test.tmp");
            var fioTestFile = ConvertPathForFIO(testFile);
            var fileSize = $"{config.FileSizeMB}M";
            var blockSize = $"{config.BlockSizeKB}K";
            
            // 根据测试类型确定读写模式
            var rwMode = config.TestType switch
            {
                TestType.SequentialRead => "read",
                TestType.SequentialWrite => "write",
                TestType.RandomRead => "randread",
                TestType.RandomWrite => "randwrite",
                TestType.Mixed => "readwrite",
                TestType.RandomMixed => "randrw",
                _ => "read"
            };

            var args = new List<string>
            {
                $"--name=disk_benchmark",
                $"--filename={fioTestFile}",
                $"--bs={blockSize}",
                $"--rw={rwMode}",
                $"--iodepth={config.QueueDepth}",
                $"--numjobs={config.ThreadCount}",
                $"--ioengine={config.IOEngine}",
                $"--group_reporting"
            };

            // 总是添加 --size 参数
            args.Add($"--size={fileSize}");
            
            if (config.TimeBased)
            {
                args.Add($"--runtime={config.DurationSeconds}");
                args.Add("--time_based");
            }

            if (config.UseDirectIO)
            {
                args.Add("--direct=1");
            }

            if (config.UseThread)
            {
                args.Add("--thread");
            }

            if (config.TestType == TestType.Mixed || config.TestType == TestType.RandomMixed)
            {
                args.Add("--rwmixread=50"); // 混合测试时读写各50%
            }

            // 将 output-format 放在最后
            args.Add("--output-format=json");

            var command = $"{config.FioPath} {string.Join(" ", args)}";
            return command;
        }

        private async Task<BenchmarkResult> RunFIOBenchmarkAsync(BenchmarkConfig config)
        {
            LogService.Info($"开始FIO测试 - 文件大小: {config.FileSizeMB}MB, 块大小: {config.BlockSizeKB}KB, IO引擎: {config.IOEngine}, 线程数: {config.ThreadCount}, 队列深度: {config.QueueDepth}");
            
            // 获取FIO版本
            string fioVersion = "";
            if (config.UseFIO)
            {
                fioVersion = GetFioVersion(config.FioPath);
            }
            
            // 创建配置的深拷贝
            var configCopy = new BenchmarkConfig
            {
                TestPath = config.TestPath,
                FileSizeMB = config.FileSizeMB,
                BlockSizeKB = config.BlockSizeKB,
                TestType = config.TestType,
                ThreadCount = config.ThreadCount,
                QueueDepth = config.QueueDepth,
                DurationSeconds = config.DurationSeconds,
                UseFIO = config.UseFIO,
                FioPath = config.FioPath,
                UseDirectIO = config.UseDirectIO,
                TimeBased = config.TimeBased,
                IOEngine = config.IOEngine,
                UseThread = config.UseThread,
                FioVersion = fioVersion
            };
            
            var result = new BenchmarkResult
            {
                TestTime = DateTime.Now,
                TestPath = config.TestPath,
                TestType = config.TestType,
                Config = configCopy,
                Status = "进行中",
                Command = GenerateCommandPreview(configCopy),
                FioVersion = fioVersion,
                ThreadCount = config.ThreadCount,
                QueueDepth = config.QueueDepth,
                BlockSizeKB = config.BlockSizeKB
            };

            await Task.Run(() =>
            {
                try
                {
                    var testFile = Path.Combine(config.TestPath, "fio_test.tmp");
                    var fioTestFile = ConvertPathForFIO(testFile);
                    var fileSize = $"{config.FileSizeMB}M";
                    var blockSize = $"{config.BlockSizeKB}K";
                    
                    // 根据测试类型确定读写模式
                    var rwMode = config.TestType switch
                    {
                        TestType.SequentialRead => "read",
                        TestType.SequentialWrite => "write",
                        TestType.RandomRead => "randread",
                        TestType.RandomWrite => "randwrite",
                        TestType.Mixed => "readwrite",
                        TestType.RandomMixed => "randrw",
                        _ => "read"
                    };

                    var args = new List<string>
                    {
                        $"--name=disk_benchmark",
                        $"--filename={fioTestFile}",
                        $"--bs={blockSize}",
                        $"--rw={rwMode}",
                        $"--iodepth={config.QueueDepth}",
                        $"--numjobs={config.ThreadCount}",
                        $"--ioengine={config.IOEngine}",
                        $"--group_reporting"
                    };

                    // 总是添加 --size 参数
                    args.Add($"--size={fileSize}");
                    
                    if (config.TimeBased)
                    {
                        args.Add($"--runtime={config.DurationSeconds}");
                        args.Add("--time_based");
                    }

                    if (config.UseDirectIO)
                    {
                        args.Add("--direct=1");
                    }

                    if (config.UseThread)
                    {
                        args.Add("--thread");
                    }

                    if (config.TestType == TestType.Mixed || config.TestType == TestType.RandomMixed)
                    {
                        args.Add("--rwmixread=50");
                    }

                    // 将 output-format 放在最后
                    args.Add("--output-format=json");

                    var command = $"{config.FioPath} {string.Join(" ", args)}";
                    LogService.Debug($"执行FIO命令: {command}");
                    ProgressUpdated?.Invoke($"正在运行FIO测试: {command}");

                    var processInfo = new ProcessStartInfo
                    {
                        FileName = config.FioPath,
                        Arguments = string.Join(" ", args),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(processInfo);
                    if (process != null)
                    {
                        LogService.Debug($"FIO进程已启动，PID: {process.Id}");
                        var output = process.StandardOutput.ReadToEnd();
                        var error = process.StandardError.ReadToEnd();
                        process.WaitForExit();

                        LogService.Debug($"FIO进程退出，退出码: {process.ExitCode}");

                        // 保存FIO输出到日志文件目录
                        try
                        {
                            var logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                            if (!Directory.Exists(logDirectory))
                            {
                                Directory.CreateDirectory(logDirectory);
                            }
                            
                            // 获取测试类型的中文名称（用于文件名）
                            var testTypeName = config.TestType switch
                            {
                                TestType.SequentialRead => "顺序读取",
                                TestType.SequentialWrite => "顺序写入",
                                TestType.RandomRead => "随机读取",
                                TestType.RandomWrite => "随机写入",
                                TestType.Mixed => "混合测试",
                                TestType.RandomMixed => "随机混合",
                                _ => "未知"
                            };
                            
                            // 将测试类型名称转换为文件名安全的格式（移除空格等特殊字符）
                            var testTypeSafe = testTypeName.Replace(" ", "_");
                            
                            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                            var fioOutputFile = Path.Combine(logDirectory, $"fio_output_{testTypeSafe}_{timestamp}.txt");
                            var fioErrorFile = Path.Combine(logDirectory, $"fio_error_{testTypeSafe}_{timestamp}.txt");
                            
                            // 保存标准输出
                            if (!string.IsNullOrEmpty(output))
                            {
                                File.WriteAllText(fioOutputFile, output);
                                LogService.Debug($"FIO输出已保存到: {fioOutputFile}");
                            }
                            
                            // 保存错误输出
                            if (!string.IsNullOrEmpty(error))
                            {
                                File.WriteAllText(fioErrorFile, error);
                                LogService.Debug($"FIO错误输出已保存到: {fioErrorFile}");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Warn($"保存FIO输出文件失败: {ex.Message}");
                        }

                        if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
                        {
                            LogService.Error($"FIO测试失败，退出码: {process.ExitCode}, 错误信息: {error}");
                            result.Status = "失败";
                            result.ErrorMessage = error;
                        }
                        else
                        {
                            // 解析FIO输出
                            ParseFIOOutput(output, result);
                            // 确保测试完成后状态更新为"完成"（除非已经设置为"失败"）
                            if (result.Status == "进行中" || string.IsNullOrEmpty(result.Status))
                            {
                                result.Status = "完成";
                            }
                            LogService.Info($"FIO测试完成 - 读取速度: {result.ReadSpeed:F2} MB/s, 写入速度: {result.WriteSpeed:F2} MB/s, 读取IOPS: {result.ReadIOPS:F0}, 写入IOPS: {result.WriteIOPS:F0}");
                        }
                    }
                    else
                    {
                        LogService.Error($"无法启动FIO进程: {config.FioPath}");
                        result.Status = "失败";
                        result.ErrorMessage = $"无法启动FIO进程: {config.FioPath}";
                    }

                    // 清理测试文件
                    if (File.Exists(testFile))
                    {
                        try
                        {
                            File.Delete(testFile);
                            LogService.Debug($"已删除FIO测试文件: {testFile}");
                        }
                        catch (Exception ex)
                        {
                            LogService.Warn($"删除FIO测试文件失败: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error($"FIO测试执行失败: {ex.Message}", ex);
                    result.Status = "失败";
                    result.ErrorMessage = ex.Message;
                }
            });

            // 确保测试完成后状态正确（如果仍然是"进行中"，则更新为"完成"）
            if (result.Status == "进行中")
            {
                result.Status = "完成";
            }

            TestCompleted?.Invoke(result);
            return result;
        }

        private void ParseFIOOutput(string output, BenchmarkResult result)
        {
            try
            {
                // 尝试解析 JSON 格式
                // FIO JSON 输出可能包含多行，需要找到 JSON 对象
                var jsonStart = output.IndexOf('{');
                var jsonEnd = output.LastIndexOf('}');
                
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var jsonStr = output.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    var json = JObject.Parse(jsonStr);
                    
                    // 查找 jobs 数组
                    var jobs = json["jobs"] as JArray;
                    if (jobs != null && jobs.Count > 0)
                    {
                        // 聚合所有 job 的结果
                        double totalReadBw = 0, totalWriteBw = 0;
                        double totalReadIops = 0, totalWriteIops = 0;
                        double totalReadLat = 0, totalWriteLat = 0;
                        int readCount = 0, writeCount = 0;
                        
                        foreach (var job in jobs)
                        {
                            LogService.Debug($"解析 job: {job.ToString()}");
                            
                            // 读取统计
                            var read = job["read"];
                            if (read != null)
                            {
                                // bw 在 FIO JSON 中单位是 KB/s
                                var bw = read["bw"]?.Value<long>() ?? 0;
                                var iops = read["iops"]?.Value<double>() ?? 0;
                                
                                LogService.Debug($"读取统计 - bw: {bw} KB/s, iops: {iops}");
                                
                                // 延迟：尝试多种可能的字段名
                                long latNs = 0;
                                var readLatNsObj = read["lat_ns"];
                                if (readLatNsObj != null)
                                {
                                    var meanObj = readLatNsObj["mean"];
                                    var avgObj = readLatNsObj["avg"];
                                    if (meanObj != null)
                                        latNs = meanObj.Value<long>();
                                    else if (avgObj != null)
                                        latNs = avgObj.Value<long>();
                                }
                                else
                                {
                                    var latObj = read["lat"];
                                    if (latObj != null)
                                    {
                                        var meanObj = latObj["mean"];
                                        var avgObj = latObj["avg"];
                                        if (meanObj != null)
                                            latNs = meanObj.Value<long>() * 1000000; // 如果是毫秒，转换为纳秒
                                        else if (avgObj != null)
                                            latNs = avgObj.Value<long>() * 1000000;
                                    }
                                }
                                
                                // bw 单位是 KB/s，转换为 MB/s
                                totalReadBw += bw / 1024.0;
                                totalReadIops += iops;
                                if (latNs > 0)
                                {
                                    totalReadLat += latNs / 1000000.0; // 纳秒转毫秒
                                }
                                readCount++;
                            }
                            else
                            {
                                LogService.Debug("未找到 read 字段");
                            }
                            
                            // 写入统计
                            var write = job["write"];
                            if (write != null)
                            {
                                var bw = write["bw"]?.Value<long>() ?? 0;
                                var iops = write["iops"]?.Value<double>() ?? 0;
                                
                                LogService.Debug($"写入统计 - bw: {bw} KB/s, iops: {iops}");
                                
                                // 延迟：尝试多种可能的字段名
                                long latNs = 0;
                                var writeLatNsObj = write["lat_ns"];
                                if (writeLatNsObj != null)
                                {
                                    var meanObj = writeLatNsObj["mean"];
                                    var avgObj = writeLatNsObj["avg"];
                                    if (meanObj != null)
                                        latNs = meanObj.Value<long>();
                                    else if (avgObj != null)
                                        latNs = avgObj.Value<long>();
                                }
                                else
                                {
                                    var latObj = write["lat"];
                                    if (latObj != null)
                                    {
                                        var meanObj = latObj["mean"];
                                        var avgObj = latObj["avg"];
                                        if (meanObj != null)
                                            latNs = meanObj.Value<long>() * 1000000;
                                        else if (avgObj != null)
                                            latNs = avgObj.Value<long>() * 1000000;
                                    }
                                }
                                
                                totalWriteBw += bw / 1024.0; // 转换为 MB/s
                                totalWriteIops += iops;
                                if (latNs > 0)
                                {
                                    totalWriteLat += latNs / 1000000.0; // 纳秒转毫秒
                                }
                                writeCount++;
                            }
                            else
                            {
                                LogService.Debug("未找到 write 字段");
                            }
                        }
                        
                        if (readCount > 0)
                        {
                            result.ReadSpeed = totalReadBw;
                            result.ReadIOPS = totalReadIops;
                            result.ReadLatency = totalReadLat / readCount;
                            LogService.Debug($"读取结果 - 速度: {result.ReadSpeed:F2} MB/s, IOPS: {result.ReadIOPS:F0}, 延迟: {result.ReadLatency:F2} ms");
                        }
                        else
                        {
                            LogService.Warn("未找到任何读取统计数据");
                        }
                        
                        if (writeCount > 0)
                        {
                            result.WriteSpeed = totalWriteBw;
                            result.WriteIOPS = totalWriteIops;
                            result.WriteLatency = totalWriteLat / writeCount;
                            LogService.Debug($"写入结果 - 速度: {result.WriteSpeed:F2} MB/s, IOPS: {result.WriteIOPS:F0}, 延迟: {result.WriteLatency:F2} ms");
                        }
                        else
                        {
                            LogService.Warn("未找到任何写入统计数据");
                        }
                        
                        LogService.Debug($"JSON解析成功 - 读取: {result.ReadSpeed:F2} MB/s, 写入: {result.WriteSpeed:F2} MB/s, 读取IOPS: {result.ReadIOPS:F0}, 写入IOPS: {result.WriteIOPS:F0}");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Warn($"JSON解析失败，尝试文本解析: {ex.Message}");
            }
            
            // 如果 JSON 解析失败，回退到文本解析（向后兼容）
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                // 解析读取速度 (MB/s) - 支持多种格式
                if (line.Contains("READ:") || line.Contains("read:"))
                {
                    // 尝试匹配 bw= 格式
                    var bwMatch = System.Text.RegularExpressions.Regex.Match(line, @"bw=([\d.]+)([KMGT]?i?B/s)");
                    if (bwMatch.Success)
                    {
                        var value = double.Parse(bwMatch.Groups[1].Value);
                        var unit = bwMatch.Groups[2].Value;
                        result.ReadSpeed = ConvertToMBps(value, unit);
                    }

                    // 解析读取IOPS - 支持多种格式
                    var iopsMatch = System.Text.RegularExpressions.Regex.Match(line, @"iops=([\d.]+)");
                    if (iopsMatch.Success)
                    {
                        result.ReadIOPS = double.Parse(iopsMatch.Groups[1].Value);
                    }

                    // 解析读取延迟 - 支持多种格式
                    var latMatch = System.Text.RegularExpressions.Regex.Match(line, @"lat\s*\(([mun]?s)\)\s*:\s*min=.*?avg=([\d.]+)");
                    if (!latMatch.Success)
                    {
                        latMatch = System.Text.RegularExpressions.Regex.Match(line, @"lat.*?avg=([\d.]+)([mun]?s)");
                    }
                    if (latMatch.Success)
                    {
                        var value = double.Parse(latMatch.Groups[latMatch.Groups.Count - 1].Value);
                        var unit = latMatch.Groups.Count > 2 ? latMatch.Groups[latMatch.Groups.Count - 1].Value : "ms";
                        if (latMatch.Groups.Count > 2 && latMatch.Groups[2].Success)
                        {
                            unit = latMatch.Groups[2].Value;
                        }
                        result.ReadLatency = ConvertToMs(value, unit);
                    }
                }

                // 解析写入速度 (MB/s) - 支持多种格式
                if (line.Contains("WRITE:") || line.Contains("write:"))
                {
                    // 尝试匹配 bw= 格式
                    var bwMatch = System.Text.RegularExpressions.Regex.Match(line, @"bw=([\d.]+)([KMGT]?i?B/s)");
                    if (bwMatch.Success)
                    {
                        var value = double.Parse(bwMatch.Groups[1].Value);
                        var unit = bwMatch.Groups[2].Value;
                        result.WriteSpeed = ConvertToMBps(value, unit);
                    }

                    // 解析写入IOPS - 支持多种格式
                    var iopsMatch = System.Text.RegularExpressions.Regex.Match(line, @"iops=([\d.]+)");
                    if (iopsMatch.Success)
                    {
                        result.WriteIOPS = double.Parse(iopsMatch.Groups[1].Value);
                    }

                    // 解析写入延迟 - 支持多种格式
                    var latMatch = System.Text.RegularExpressions.Regex.Match(line, @"lat\s*\(([mun]?s)\)\s*:\s*min=.*?avg=([\d.]+)");
                    if (!latMatch.Success)
                    {
                        latMatch = System.Text.RegularExpressions.Regex.Match(line, @"lat.*?avg=([\d.]+)([mun]?s)");
                    }
                    if (latMatch.Success)
                    {
                        var value = double.Parse(latMatch.Groups[latMatch.Groups.Count - 1].Value);
                        var unit = latMatch.Groups.Count > 2 ? latMatch.Groups[latMatch.Groups.Count - 1].Value : "ms";
                        if (latMatch.Groups.Count > 2 && latMatch.Groups[2].Success)
                        {
                            unit = latMatch.Groups[2].Value;
                        }
                        result.WriteLatency = ConvertToMs(value, unit);
                    }
                }
            }
        }

        private double ConvertToMBps(double value, string unit)
        {
            return unit.ToUpper() switch
            {
                "B/S" => value / 1024.0 / 1024.0,
                "KB/S" or "KIB/S" => value / 1024.0,
                "MB/S" or "MIB/S" => value,
                "GB/S" or "GIB/S" => value * 1024.0,
                "TB/S" or "TIB/S" => value * 1024.0 * 1024.0,
                _ => value / 1024.0 / 1024.0
            };
        }

        private double ConvertToMs(double value, string unit)
        {
            return unit.ToLower() switch
            {
                "ns" => value / 1000000.0,
                "us" => value / 1000.0,
                "ms" => value,
                "s" => value * 1000.0,
                _ => value
            };
        }
    }
}


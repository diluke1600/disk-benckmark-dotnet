#nullable enable
using System;
using NLog;

namespace DiskBenchmark.Services
{
    public static class LogService
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static void Info(string message)
        {
            Logger.Info(message);
        }

        public static void Debug(string message)
        {
            Logger.Debug(message);
        }

        public static void Warn(string message)
        {
            Logger.Warn(message);
        }

        public static void Error(string message, Exception? exception = null)
        {
            if (exception != null)
            {
                Logger.Error(exception, message);
            }
            else
            {
                Logger.Error(message);
            }
        }

        public static void Fatal(string message, Exception? exception = null)
        {
            if (exception != null)
            {
                Logger.Fatal(exception, message);
            }
            else
            {
                Logger.Fatal(message);
            }
        }
    }
}



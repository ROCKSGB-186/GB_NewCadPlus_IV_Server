using System;
using System.IO;
using System.Text;
using System.Threading;

namespace GB_NewCadPlus_IV_Server.Services
{
    /// <summary>
    /// 按小时滚动的文件日志记录器，每个小时生成一个新的日志文件。
    /// 文件名格式：{prefix}_20250127_14.log
    /// </summary>
    public class HourlyFileLogger : IDisposable
    {
        private readonly string _logDirectory;
        private readonly string _fileNamePrefix;
        private readonly object _lock = new object();
        private StreamWriter? _writer;
        private string? _currentFilePath;
        private string _currentHourKey = string.Empty; // 格式：yyyyMMdd_HH

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logDirectory">日志文件存放目录</param>
        /// <param name="fileNamePrefix">日志文件名前缀，如 "GraphicsOperation"</param>
        public HourlyFileLogger(string logDirectory, string fileNamePrefix = "Operation")
        {
            _logDirectory = logDirectory ?? throw new ArgumentNullException(nameof(logDirectory));
            _fileNamePrefix = fileNamePrefix;

            if (!Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);
        }

        /// <summary>
        /// 写入一条日志（自动附加时间戳）
        /// </summary>
        public void WriteLine(string message)
        {
            WriteLine(LogLevel.Info, message);
        }

        /// <summary>
        /// 带级别的日志写入
        /// </summary>
        public void WriteLine(LogLevel level, string message)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string levelStr = level switch
            {
                LogLevel.Debug => "DEBUG",
                LogLevel.Warning => "WARN",
                LogLevel.Error => "ERROR",
                _ => "INFO"
            };
            string line = $"[{timestamp}] [{levelStr}] {message}";

            lock (_lock)
            {
                try
                {
                    EnsureWriter();
                    _writer?.WriteLine(line);
                    _writer?.Flush();
                }
                catch
                {
                    // 日志写入失败不应影响主业务
                }
            }
        }

        /// <summary>
        /// 确保当前 Writer 指向正确的小时文件
        /// </summary>
        private void EnsureWriter()
        {
            string hourKey = DateTime.Now.ToString("yyyyMMdd_HH");
            if (hourKey == _currentHourKey && _writer != null)
                return;

            // 关闭旧的 writer
            CloseWriter();

            // 构建新文件路径
            string fileName = $"{_fileNamePrefix}_{hourKey}.log";
            _currentFilePath = Path.Combine(_logDirectory, fileName);
            _currentHourKey = hourKey;

            // 创建新的 writer（追加模式，UTF-8 编码）
            var stream = new FileStream(_currentFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        }

        private void CloseWriter()
        {
            try
            {
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch { }
            _writer = null;
        }

        public void Dispose()
        {
            CloseWriter();
        }
        /// <summary>
        /// 清理超过指定天数的旧日志文件
        /// </summary>
        public void CleanOldLogs(int retainDays = 30)
        {
            try
            {
                if (!Directory.Exists(_logDirectory)) return;

                var cutoff = DateTime.Now.AddDays(-retainDays);
                var files = Directory.GetFiles(_logDirectory, $"{_fileNamePrefix}_*.log");
                foreach (var file in files)
                {
                    try
                    {
                        if (File.GetCreationTime(file) < cutoff)
                            File.Delete(file);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// 日志级别枚举
    /// </summary>
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }
}
using System;
using System.IO;
using System.Text;
using System.Threading;
using static GB_NewCadPlus_IV_Server.Services.HourlyFileLogger;

namespace GB_NewCadPlus_IV_Server.Services
{
    /// <summary>
    /// 日志文件写入接口，定义了写入日志的方法。可以通过依赖注入提供不同的实现，例如直接使用 HourlyFileLogger 或者其他日志服务。
    /// </summary>
    public interface IFileLogService
    {
        void WriteLine(string category, string level, string message);
    }
    /// <summary>
    /// 按小时滚动的文件日志记录器，每个小时生成一个新的日志文件。
    /// 文件名格式：{prefix}_20250127_14.log
    /// </summary>
    public class HourlyFileLogger : IFileLogService, IDisposable
    {
        /// <summary>
        /// 日志文件根目录，从配置中读取，默认为当前应用程序目录下的 "Logs" 文件夹。
        /// </summary>
        private readonly string _rootDir;
        /// <summary>
        /// 线程同步对象，确保多线程环境下日志写入的线程安全。使用 SemaphoreSlim 以支持异步写入场景。
        /// </summary>
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        /// <summary>
        /// 构造函数，接受配置对象以获取日志文件根目录。确保目录存在，如果不存在则创建。
        /// </summary>
        /// <param name="configuration"></param>
        public HourlyFileLogger(IConfiguration configuration)
        {
            _rootDir = configuration["Logging:LogRootDir"]
                       ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(_rootDir);
        }
        /// <summary>
        /// 写入一条日志，包含类别、级别和消息内容。日志文件名根据类别和当前时间自动生成，格式为 "{category}_log_yyyyMMddHH.txt"。
        /// </summary>
        /// <param name="category"></param>
        /// <param name="level"></param>
        /// <param name="message"></param>
        public void WriteLine(string category, string level, string message)
        {
            string fileName = $"{category}_log_{DateTime.Now:yyyyMMddHH}.txt";
            string fullPath = Path.Combine(_rootDir, fileName);
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";

            _semaphore.Wait();
            try
            {
                File.AppendAllText(fullPath, line + Environment.NewLine);
            }
            finally { _semaphore.Release(); }
        }
        
        /// <summary>
        /// 日志文件存放目录
        /// </summary>
        private readonly string _logDirectory;
        /// <summary>
        /// 日志文件名前缀，如 "GraphicsOperation"，最终文件名将包含时间戳，如 "GraphicsOperation_20250127_14.log"
        /// </summary>
        private readonly string _fileNamePrefix;
        /// <summary>
        /// 锁对象，确保多线程环境下日志写入的线程安全
        /// </summary>
        private readonly object _lock = new object();
        /// <summary>
        /// 当前的 StreamWriter，用于写入日志文件。每小时切换一次文件时会重新创建。
        /// </summary>
        private StreamWriter? _writer;
        /// <summary>
        /// 当前日志文件的完整路径，格式：{logDirectory}\{fileNamePrefix}_yyyyMMdd_HH.log
        /// </summary>
        private string? _currentFilePath;
        /// <summary>
        /// 当前小时的标识，用于判断是否需要切换日志文件。格式：yyyyMMdd_HH，例如 "20250127_14"
        /// </summary>
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

        /// <summary>
        /// 关闭当前的 StreamWriter，释放资源
        /// </summary>
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

        /// <summary>
        /// 实现 IDisposable 接口，确保资源正确释放
        /// </summary>
        public void Dispose() => _semaphore?.Dispose();

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
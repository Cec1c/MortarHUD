using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MortarHUD.Core.Diagnostics;

/// <summary>
/// 把日志写到 <c>%AppData%/MortarHUD/Logs/yyyy-MM-dd.log</c>（TDD §39）。
/// </summary>
/// <remarks>
/// <para>
/// 自己实现而不是引入 Serilog / NLog：需要的功能只有「按天分文件 + 后台线程写」，
/// 为此多背一个第三方依赖不划算，也会让发布体积变大。
/// </para>
/// <para>
/// 写盘在后台线程完成，绝不阻塞调用方——采集链路是按键触发的，
/// 任何一次磁盘卡顿都会直接表现为「按了没反应」。
/// </para>
/// </remarks>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly BlockingCollection<string> _queue = new(new ConcurrentQueue<string>(), 4096);
    private readonly string _directory;
    private readonly Thread _writer;
    private readonly int _retainedDays;

    private bool _disposed;

    public FileLoggerProvider(string? directory = null, int retainedDays = 14)
    {
        _directory = directory ?? Configuration.AppPaths.LogsDirectory;
        _retainedDays = retainedDays;

        Directory.CreateDirectory(_directory);

        _writer = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "MortarHUD.LogWriter",
        };

        _writer.Start();
        CleanupOldLogs();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    private void Enqueue(string line)
    {
        if (_disposed)
        {
            return;
        }

        // 队列满了就丢弃而不是阻塞：日志不该拖慢采集。
        _queue.TryAdd(line);
    }

    private void WriterLoop()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            try
            {
                var path = Path.Combine(_directory, $"{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 日志写不进去不能把程序搞崩。
                System.Diagnostics.Debug.WriteLine($"日志写入失败：{ex.Message}");
            }
        }
    }

    private void CleanupOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-_retainedDays);

            foreach (var file in Directory.EnumerateFiles(_directory, "*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清理失败无关紧要。
            System.Diagnostics.Debug.WriteLine($"清理旧日志失败：{ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.CompleteAdding();
        _writer.Join(TimeSpan.FromSeconds(2));
        _queue.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || formatter is null)
            {
                return;
            }

            var builder = new StringBuilder(160);
            builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            builder.Append(" [").Append(Describe(logLevel)).Append(']');
            builder.Append(" [").Append(_category).Append("] ");
            builder.Append(formatter(state, exception));

            if (exception is not null)
            {
                builder.AppendLine();
                builder.Append(exception);
            }

            _provider.Enqueue(builder.ToString());
        }

        private static string Describe(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };
    }
}

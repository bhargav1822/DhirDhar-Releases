using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Logging;

/// <summary>
/// A minimal file logger provider that appends log lines to a rolling daily log file.
/// It exists so the application has durable logs without third-party dependencies.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly object _gate = new();

    public FileLoggerProvider(string directory)
    {
        _directory = directory;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(_directory, categoryName, _gate);
    }

    public void Dispose()
    {
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _directory;
        private readonly string _categoryName;
        private readonly object _gate;

        public FileLogger(string directory, string categoryName, object gate)
        {
            _directory = directory;
            _categoryName = categoryName;
            _gate = gate;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {_categoryName}: {message}";

            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            var fileName = $"app-{DateTime.Now:yyyyMMdd}.log";
            var fullPath = Path.Combine(_directory, fileName);

            lock (_gate)
            {
                try
                {
                    Directory.CreateDirectory(_directory);
                    using var stream = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
                    writer.WriteLine(line);
                    writer.Flush();
                }
                catch
                {
                    // Logging must never crash the application.
                }
            }
        }
    }
}

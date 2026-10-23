using Microsoft.Extensions.Logging;
using Recora.Core.Paths;

namespace Recora.Core.Logging;

/// <summary>
/// Simple rolling file logger under %LOCALAPPDATA%\Recora\logs.
/// Never logs meeting content — only operational events.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly RecoraPaths _paths;
    private readonly object _gate = new();

    public FileLoggerProvider(RecoraPaths paths)
    {
        _paths = paths;
        _paths.EnsureCreated();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    public void Dispose() { }

    internal void Write(string category, LogLevel level, string message, Exception? exception)
    {
        var line = $"{DateTimeOffset.UtcNow:O}\t{level}\t{category}\t{Sanitize(message)}";
        if (exception is not null)
            line += $"\t{Sanitize(exception.GetType().Name)}: {Sanitize(exception.Message)}";

        lock (_gate)
        {
            var file = Path.Combine(_paths.Logs, $"tray-{DateTime.UtcNow:yyyyMMdd}.log");
            File.AppendAllText(file, line + Environment.NewLine);
        }
    }

    private static string Sanitize(string text)
    {
        // Strip control chars; keep operational text only.
        return string.Concat(text.Where(c => !char.IsControl(c) || c is '\t'));
    }
}

internal sealed class FileLogger : ILogger
{
    private readonly string _category;
    private readonly FileLoggerProvider _provider;

    public FileLogger(string category, FileLoggerProvider provider)
    {
        _category = category;
        _provider = provider;
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
        if (!IsEnabled(logLevel)) return;
        _provider.Write(_category, logLevel, formatter(state, exception), exception);
    }
}

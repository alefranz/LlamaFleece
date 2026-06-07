using System.Text;
using Microsoft.Extensions.Logging;

internal sealed class RuntimeDiagnosticLog : IDisposable
{
    private readonly object _sync = new();
    private readonly StreamWriter? _writer;

    private RuntimeDiagnosticLog(string filePath, StreamWriter? writer)
    {
        FilePath = filePath;
        _writer = writer;
    }

    public string FilePath { get; }

    public bool IsEnabled => _writer is not null;

    public static RuntimeDiagnosticLog CreateDefault()
    {
        Exception? lastFailure = null;

        foreach (var candidatePath in GetCandidatePaths())
        {
            try
            {
                var directoryPath = Path.GetDirectoryName(candidatePath);
                if (!string.IsNullOrWhiteSpace(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                var stream = new FileStream(candidatePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                {
                    AutoFlush = true
                };

                return new RuntimeDiagnosticLog(candidatePath, writer);
            }
            catch (Exception ex)
            {
                lastFailure = ex;
            }
        }

        throw new InvalidOperationException("Runtime diagnostics could not open a writable log file.", lastFailure);
    }

    public static RuntimeDiagnosticLog CreateDisabled(string filePath)
    {
        return new RuntimeDiagnosticLog(filePath, null);
    }

    public RuntimeDiagnosticLoggerProvider CreateLoggerProvider()
    {
        return new RuntimeDiagnosticLoggerProvider(this);
    }

    public void WriteEntry(LogLevel logLevel, string category, string message, Exception? exception = null)
    {
        if (_writer is null)
        {
            return;
        }

        var safeCategory = string.IsNullOrWhiteSpace(category) ? "Runtime" : category.Trim();
        var safeMessage = string.IsNullOrWhiteSpace(message) ? "(no message)" : message.Trim();

        lock (_sync)
        {
            _writer.Write('[');
            _writer.Write(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
            _writer.Write("] [");
            _writer.Write(logLevel);
            _writer.Write("] ");
            _writer.Write(safeCategory);
            _writer.Write(": ");
            _writer.WriteLine(safeMessage);

            if (exception is not null)
            {
                _writer.WriteLine(exception);
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
        }
    }

    internal static string GetDefaultFileName()
    {
        return "llamafleece-runtime.log";
    }

    internal static string[] GetCandidatePaths()
    {
        var fileName = GetDefaultFileName();

        return new[]
        {
            Path.Combine(AppContext.BaseDirectory, "logs", fileName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LlamaFleece", "logs", fileName),
            Path.Combine(Path.GetTempPath(), "LlamaFleece", "logs", fileName)
        };
    }
}

internal sealed class RuntimeDiagnosticLoggerProvider : ILoggerProvider
{
    private readonly RuntimeDiagnosticLog _diagnosticLog;

    public RuntimeDiagnosticLoggerProvider(RuntimeDiagnosticLog diagnosticLog)
    {
        _diagnosticLog = diagnosticLog;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new RuntimeDiagnosticLogger(categoryName, _diagnosticLog);
    }

    public void Dispose()
    {
    }
}

internal sealed class RuntimeDiagnosticLogger : ILogger
{
    private readonly string _categoryName;
    private readonly RuntimeDiagnosticLog _diagnosticLog;

    public RuntimeDiagnosticLogger(string categoryName, RuntimeDiagnosticLog diagnosticLog)
    {
        _categoryName = categoryName;
        _diagnosticLog = diagnosticLog;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return NullScope.Instance;
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
        ArgumentNullException.ThrowIfNull(formatter);

        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is not null)
        {
            message = exception.Message;
        }

        _diagnosticLog.WriteEntry(logLevel, _categoryName, message, exception);
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
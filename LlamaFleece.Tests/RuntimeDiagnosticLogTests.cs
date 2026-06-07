using Microsoft.Extensions.Logging;
using Xunit;

public class RuntimeDiagnosticLogTests
{
    [Fact]
    public void WriteEntry_AppendsTimestampedLineAndException()
    {
        using var exportDirectory = new TestExportDirectory();
        var logPath = Path.Combine(exportDirectory.Path, "runtime.log");
        using (var writer = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)))
        {
            writer.AutoFlush = true;

            using var diagnosticLog = CreateDiagnosticLogForTests(logPath, writer);
            diagnosticLog.WriteEntry(LogLevel.Warning, "Tests", "Something happened.", new InvalidOperationException("boom"));
        }

        var output = File.ReadAllText(logPath);
        Assert.Contains("[Warning] Tests: Something happened.", output, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException: boom", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateLoggerProvider_WritesFormattedLoggerMessages()
    {
        using var exportDirectory = new TestExportDirectory();
        var logPath = Path.Combine(exportDirectory.Path, "runtime.log");
        using (var writer = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)))
        {
            writer.AutoFlush = true;

            using var diagnosticLog = CreateDiagnosticLogForTests(logPath, writer);
            using var provider = diagnosticLog.CreateLoggerProvider();
            var logger = provider.CreateLogger("Tests.Category");

            logger.LogInformation("Host started on {Address}", "localhost:5000");
        }

        var output = File.ReadAllText(logPath);
        Assert.Contains("[Information] Tests.Category: Host started on localhost:5000", output, StringComparison.Ordinal);
    }

    private static RuntimeDiagnosticLog CreateDiagnosticLogForTests(string logPath, StreamWriter writer)
    {
        var constructor = typeof(RuntimeDiagnosticLog).GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            binder: null,
            new[] { typeof(string), typeof(StreamWriter) },
            modifiers: null);

        Assert.NotNull(constructor);
        return (RuntimeDiagnosticLog)constructor!.Invoke(new object[] { logPath, writer });
    }
}
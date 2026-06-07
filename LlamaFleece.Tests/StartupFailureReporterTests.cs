using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Xunit;

public class StartupFailureReporterTests
{
    [Fact]
    public void WriteFriendlyMessage_FormatsConfigurationValidationWithoutStackTrace()
    {
        var exception = new OptionsValidationException(
            ProxyOptions.SectionName,
            typeof(ProxyOptions),
            new[]
            {
                "Proxy:ListenPort must be between 1 and 65535.",
                "Proxy:UpstreamUrl must be an absolute URI."
            });

        using var writer = new StringWriter();

        var exitCode = StartupFailureReporter.WriteFriendlyMessage(exception, writer);
        var output = writer.ToString();

        Assert.Equal(StartupFailureReporter.FailureExitCode, exitCode);
        Assert.Contains("LlamaFleece could not start.", output, StringComparison.Ordinal);
        Assert.Contains("Configuration is invalid.", output, StringComparison.Ordinal);
        Assert.Contains("- Proxy:ListenPort must be between 1 and 65535.", output, StringComparison.Ordinal);
        Assert.Contains("appsettings.json", output, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", output, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(OptionsValidationException), output, StringComparison.Ordinal);
    }

    [Fact]
    public void IsLikelyPortBindingFailure_DetectsAddressAlreadyInUseSocketException()
    {
        var exception = new IOException(
            "Failed to bind to address.",
            new SocketException((int)SocketError.AddressAlreadyInUse));

        Assert.True(StartupFailureReporter.IsLikelyPortBindingFailure(exception));
    }

    [Fact]
    public void WriteFriendlyMessage_FormatsPortBindingFailure()
    {
        var exception = new StartupPortBindingException(
            "http://localhost:5000",
            new IOException("Failed to bind to address.", new SocketException((int)SocketError.AddressAlreadyInUse)));

        using var writer = new StringWriter();
        StartupFailureReporter.WriteFriendlyMessage(exception, writer);

        var output = writer.ToString();

        Assert.Contains("The proxy could not listen on http://localhost:5000.", output, StringComparison.Ordinal);
        Assert.Contains("Another process is already using that address.", output, StringComparison.Ordinal);
        Assert.Contains("Proxy:ListenHost / Proxy:ListenPort", output, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(SocketException), output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteFriendlyMessage_FormatsPersistenceRestoreFailure()
    {
        var exception = new StartupPersistenceRestoreException(
            @"C:\temp\state\session-history.json",
            new InvalidDataException("Persisted session file version '2' is not supported."));

        using var writer = new StringWriter();
        StartupFailureReporter.WriteFriendlyMessage(exception, writer);

        var output = writer.ToString();

        Assert.Contains("The persisted session could not be restored", output, StringComparison.Ordinal);
        Assert.Contains("Persisted session file version '2' is not supported.", output, StringComparison.Ordinal);
        Assert.Contains("Proxy:Persistence:Enabled=false", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteFriendlyMessage_FormatsPersistencePreflightFailure()
    {
        var exception = new StartupPersistencePreflightException(
            @"C:\temp\state\session-history.json",
            new IOException("The process cannot access the file because it is being used by another process."));

        using var writer = new StringWriter();
        StartupFailureReporter.WriteFriendlyMessage(exception, writer);

        var output = writer.ToString();

        Assert.Contains("The persisted session file could not be prepared", output, StringComparison.Ordinal);
        Assert.Contains("used by another process", output, StringComparison.Ordinal);
        Assert.Contains("parent directory is writable", output, StringComparison.Ordinal);
        Assert.Contains("Proxy:Persistence:Enabled=false", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteFriendlyMessage_FormatsTerminalInitializationFailure()
    {
        var exception = new StartupTerminalInitializationException(
            new InvalidOperationException("Console input is not available."));

        using var writer = new StringWriter();
        StartupFailureReporter.WriteFriendlyMessage(exception, writer);

        var output = writer.ToString();

        Assert.Contains("The interactive terminal UI could not be initialized.", output, StringComparison.Ordinal);
        Assert.Contains("Console input is not available.", output, StringComparison.Ordinal);
        Assert.Contains("real interactive terminal", output, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), output, StringComparison.Ordinal);
    }
}
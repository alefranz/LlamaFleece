using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Xunit;

[CollectionDefinition("StartupIntegration", DisableParallelization = true)]
public sealed class StartupIntegrationCollectionDefinition
{
}

[Collection("StartupIntegration")]
public sealed class StartupIntegrationTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    public async Task Main_WhenListenPortIsInvalid_ReturnsFriendlyValidationError(string invalidPort)
    {
        var result = await StartupEntryPointTestHarness.RunAsync(
            $"--{ProxyOptions.SectionName}:ListenPort={invalidPort}",
            $"--{ProxyOptions.SectionName}:UpstreamUrl=http://127.0.0.1:8123",
            $"--{ProxyOptions.SectionName}:Persistence:Enabled=false");

        Assert.Equal(StartupFailureReporter.FailureExitCode, result.ExitCode);
        Assert.Contains("LlamaFleece could not start.", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("Configuration is invalid.", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("- Proxy:ListenPort must be between 1 and 65535.", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("Fix the values above in appsettings.json", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(OptionsValidationException), result.StandardError, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-a-uri", "Proxy:UpstreamUrl must be an absolute URI.")]
    [InlineData("ftp://upstream.test/v1", "Proxy:UpstreamUrl must use http or https.")]
    [InlineData("https://user:password@upstream.test/v1", "Proxy:UpstreamUrl cannot embed user info.")]
    [InlineData("https://upstream.test/v1?debug=true", "Proxy:UpstreamUrl cannot include a query string or fragment.")]
    public async Task Main_WhenUpstreamUrlIsInvalid_ReturnsFriendlyValidationError(string upstreamUrl, string expectedFailure)
    {
        var result = await StartupEntryPointTestHarness.RunAsync(
            $"--{ProxyOptions.SectionName}:ListenPort=5100",
            $"--{ProxyOptions.SectionName}:UpstreamUrl={upstreamUrl}",
            $"--{ProxyOptions.SectionName}:Persistence:Enabled=false");

        Assert.Equal(StartupFailureReporter.FailureExitCode, result.ExitCode);
        Assert.Contains("LlamaFleece could not start.", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("Configuration is invalid.", result.StandardError, StringComparison.Ordinal);
        Assert.Contains(expectedFailure, result.StandardError, StringComparison.Ordinal);
        Assert.Contains("Fix the values above in appsettings.json", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(OptionsValidationException), result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Main_WhenListenPortIsAlreadyInUse_ReturnsFriendlyPortBindingError()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Server.ExclusiveAddressUse = true;
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var result = await StartupEntryPointTestHarness.RunAsync(
            $"--{ProxyOptions.SectionName}:ListenHost=127.0.0.1",
            $"--{ProxyOptions.SectionName}:ListenPort={port}",
            $"--{ProxyOptions.SectionName}:UpstreamUrl=http://127.0.0.1:8123",
            $"--{ProxyOptions.SectionName}:Persistence:Enabled=false");

        Assert.Equal(StartupFailureReporter.FailureExitCode, result.ExitCode);
        Assert.Contains("LlamaFleece could not start.", result.StandardError, StringComparison.Ordinal);
        Assert.Contains($"The proxy could not listen on http://127.0.0.1:{port}.", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("Another process is already using that address.", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("Proxy:ListenHost / Proxy:ListenPort", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(SocketException), result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Main_WhenPersistedSessionJsonIsCorrupted_ReturnsFriendlyPersistenceRestoreError()
    {
        using var exportDirectory = new TestExportDirectory();
        var persistencePath = CreatePersistenceSessionFile(exportDirectory, "{ invalid json");

        var result = await RunWithPersistenceAsync(persistencePath);

        Assert.Equal(StartupFailureReporter.FailureExitCode, result.ExitCode);
        Assert.Contains("LlamaFleece could not start.", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("The persisted session could not be restored", result.StandardError, StringComparison.Ordinal);
        Assert.Contains(persistencePath, result.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Proxy:Persistence:Enabled=false", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(JsonException), result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Main_WhenPersistedSessionVersionIsUnsupported_ReturnsFriendlyPersistenceRestoreError()
    {
        using var exportDirectory = new TestExportDirectory();
        var persistencePath = CreatePersistenceSessionFile(
            exportDirectory,
            JsonSerializer.Serialize(
                new InteractionPersistenceDocument
                {
                    Version = InteractionPersistenceDocument.CurrentVersion + 1,
                    PersistedAtUtc = new DateTime(2026, 5, 29, 12, 0, 0, DateTimeKind.Utc)
                },
                InteractionExportService.CreateJsonOptions()));

        var result = await RunWithPersistenceAsync(persistencePath);

        Assert.Equal(StartupFailureReporter.FailureExitCode, result.ExitCode);
        Assert.Contains("The persisted session could not be restored", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("Persisted session file version '2' is not supported.", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("Proxy:Persistence:Enabled=false", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidDataException), result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Main_WhenPersistenceDirectoryCannotBePrepared_ReturnsFriendlyPersistencePreflightError()
    {
        using var exportDirectory = new TestExportDirectory();
        var blockedDirectoryPath = Path.Combine(exportDirectory.Path, "state");
        File.WriteAllText(blockedDirectoryPath, "blocked");
        var persistencePath = Path.Combine(blockedDirectoryPath, "session-history.json");

        var result = await RunWithPersistenceAsync(persistencePath);

        Assert.Equal(StartupFailureReporter.FailureExitCode, result.ExitCode);
        Assert.Contains("The persisted session file could not be prepared", result.StandardError, StringComparison.Ordinal);
        Assert.Contains(persistencePath, result.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("parent directory is writable", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("Proxy:Persistence:Enabled=false", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(IOException), result.StandardError, StringComparison.Ordinal);
    }

    private sealed record StartupRunResult(int ExitCode, string StandardError);

    private static async Task<StartupRunResult> RunWithPersistenceAsync(string persistencePath)
    {
        return await StartupEntryPointTestHarness.RunAsync(
            $"--{ProxyOptions.SectionName}:ListenPort=5100",
            $"--{ProxyOptions.SectionName}:UpstreamUrl=http://127.0.0.1:8123",
            $"--{ProxyOptions.SectionName}:Persistence:Enabled=true",
            $"--{ProxyOptions.SectionName}:Persistence:SessionFilePath={persistencePath}");
    }

    private static string CreatePersistenceSessionFile(TestExportDirectory exportDirectory, string contents)
    {
        var persistencePath = Path.Combine(exportDirectory.Path, "state", "session-history.json");
        Directory.CreateDirectory(Path.GetDirectoryName(persistencePath)!);
        File.WriteAllText(persistencePath, contents);
        return persistencePath;
    }

    private static class StartupEntryPointTestHarness
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);

        public static async Task<StartupRunResult> RunAsync(params string[] args)
        {
            await Gate.WaitAsync();

            var originalError = Console.Error;
            using var errorWriter = new StringWriter();
            using var environmentScope = new StartupEnvironmentScope();

            Console.SetError(errorWriter);

            try
            {
                var exitCode = await StartupEntryPoint.RunAsync(args);
                return new StartupRunResult(exitCode, errorWriter.ToString());
            }
            finally
            {
                Console.SetError(originalError);
                Gate.Release();
            }
        }
    }

    private sealed class StartupEnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues = CaptureOriginalValues();

        public StartupEnvironmentScope()
        {
            foreach (var key in _originalValues.Keys)
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }

        public void Dispose()
        {
            foreach (var entry in _originalValues)
            {
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
        }

        private static Dictionary<string, string?> CaptureOriginalValues()
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                if (entry.Key is not string key)
                {
                    continue;
                }

                if (!key.StartsWith("Proxy__", StringComparison.OrdinalIgnoreCase) &&
                    !key.Equals("Port", StringComparison.OrdinalIgnoreCase) &&
                    !key.Equals("TargetUrl", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                values[key] = entry.Value as string;
            }

            return values;
        }
    }
}
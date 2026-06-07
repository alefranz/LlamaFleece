using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        WebApplication? app = null;
        RuntimeDiagnosticLog? diagnosticLog = null;
        RuntimeDiagnosticLoggerProvider? diagnosticLoggerProvider = null;
        CancellationTokenSource? tuiCts = null;
        ConsoleCancelEventHandler? cancelKeyHandler = null;
        UnhandledExceptionEventHandler? unhandledExceptionHandler = null;
        EventHandler<UnobservedTaskExceptionEventArgs>? unobservedTaskHandler = null;
        CancellationTokenRegistration applicationStoppingRegistration = default;
        CancellationTokenRegistration applicationStoppedRegistration = default;
        Task? appTask = null;
        Task? tuiTask = null;
        var hostStarted = false;
        Exception? startupFailure = null;

        try
        {
            var builder = WebApplication.CreateBuilder(args);
            diagnosticLog = CreateRuntimeDiagnosticLog();
            builder.Logging.ClearProviders();
            diagnosticLoggerProvider = diagnosticLog.CreateLoggerProvider();
            builder.Logging.AddProvider(diagnosticLoggerProvider);
            builder.Logging.SetMinimumLevel(LogLevel.Information);
            builder.Logging.AddFilter((category, level) => ShouldWriteDiagnosticLog(category, level));

            diagnosticLog.WriteEntry(LogLevel.Information, nameof(Program), $"Starting LlamaFleece. Runtime diagnostics: {diagnosticLog.FilePath}");

            var proxyOptions = ProxyOptions.LoadAndValidate(builder.Configuration);
            var upstreamRequestHeaderInjection = UpstreamRequestHeaderInjection.Create(proxyOptions);
            var interactionPersistenceService = CreatePersistenceService(proxyOptions);

            ConfigureHost(builder, proxyOptions, upstreamRequestHeaderInjection);

            app = builder.Build();
            app.UseMiddleware<LoggingMiddleware>();
            app.MapReverseProxy();

            TuiManager.SetReplayService(app.Services.GetRequiredService<IInteractionReplayService>());
            TuiManager.ConfigureSessionSummaryPricing(proxyOptions.Pricing);
            TuiManager.ConfigurePersistence(interactionPersistenceService);
            RestorePersistedSession(interactionPersistenceService);

            await StartHostAsync(app, proxyOptions);
            hostStarted = true;

            var appLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            tuiCts = new CancellationTokenSource();
            diagnosticLog.WriteEntry(
                LogLevel.Information,
                nameof(Program),
                $"Host started. Listening on http://{proxyOptions.GetDisplayListenHost()}:{proxyOptions.ListenPort}. Persistence enabled: {proxyOptions.Persistence.Enabled}.");

            applicationStoppingRegistration = appLifetime.ApplicationStopping.Register(() =>
            {
                diagnosticLog.WriteEntry(
                    ApplicationShutdownCoordinator.IsShutdownRequested ? LogLevel.Information : LogLevel.Warning,
                    nameof(Program),
                    $"Host is stopping. Requested={ApplicationShutdownCoordinator.IsShutdownRequested}. Reason={FormatShutdownReason(ApplicationShutdownCoordinator.ShutdownReason)}");
            });

            applicationStoppedRegistration = appLifetime.ApplicationStopped.Register(() =>
            {
                diagnosticLog.WriteEntry(
                    LogLevel.Information,
                    nameof(Program),
                    $"Host stopped. Requested={ApplicationShutdownCoordinator.IsShutdownRequested}. Reason={FormatShutdownReason(ApplicationShutdownCoordinator.ShutdownReason)}");
            });

            unhandledExceptionHandler = (_, eventArgs) =>
            {
                diagnosticLog.WriteEntry(
                    LogLevel.Critical,
                    nameof(Program),
                    $"Unhandled exception reached AppDomain. IsTerminating={eventArgs.IsTerminating}.",
                    eventArgs.ExceptionObject as Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += unhandledExceptionHandler;

            unobservedTaskHandler = (_, eventArgs) =>
            {
                diagnosticLog.WriteEntry(
                    LogLevel.Error,
                    nameof(Program),
                    "Unobserved task exception.",
                    eventArgs.Exception);
            };
            TaskScheduler.UnobservedTaskException += unobservedTaskHandler;

            void BeginShutdown(string? reason)
            {
                diagnosticLog.WriteEntry(LogLevel.Information, nameof(Program), $"Shutdown requested. Reason={FormatShutdownReason(reason)}");
                TuiManager.RecordStatusMessage("Shutting down...", isError: false);
                TuiManager.FlushPersistedSession();
                appLifetime.StopApplication();
                tuiCts.Cancel();
            }

            ApplicationShutdownCoordinator.Configure(BeginShutdown);

            cancelKeyHandler = (s, e) =>
            {
                e.Cancel = true;
                ApplicationShutdownCoordinator.RequestShutdown("Ctrl+C pressed.");
            };

            Console.CancelKeyPress += cancelKeyHandler;

            tuiTask = TuiManager.RunAsync(tuiCts.Token);
            appTask = app.WaitForShutdownAsync();

            AppendStartupMessages(proxyOptions, upstreamRequestHeaderInjection, interactionPersistenceService);
            TuiManager.RecordStatusMessage("Waiting for requests...", isError: false, appendToLog: false);

            var completedTask = await Task.WhenAny(appTask, tuiTask);
            if (completedTask == appTask)
            {
                if (!ApplicationShutdownCoordinator.IsShutdownRequested)
                {
                    diagnosticLog.WriteEntry(LogLevel.Warning, nameof(Program), "The host shutdown task completed before any explicit shutdown request was recorded.");
                }

                await appTask;
            }
            else
            {
                if (!ApplicationShutdownCoordinator.IsShutdownRequested)
                {
                    diagnosticLog.WriteEntry(LogLevel.Warning, nameof(Program), "The terminal UI task completed before any explicit shutdown request was recorded.");
                }

                await tuiTask;
            }

            diagnosticLog.WriteEntry(LogLevel.Information, nameof(Program), "Main loop completed normally.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            diagnosticLog?.WriteEntry(LogLevel.Information, nameof(Program), "Startup or shutdown was canceled.");
            return 0;
        }
        catch (Exception ex)
        {
            startupFailure = ex;
            diagnosticLog?.WriteEntry(LogLevel.Critical, nameof(Program), "Startup failed with an unhandled exception.", ex);
            return StartupFailureReporter.WriteFriendlyMessage(ex);
        }
        finally
        {
            applicationStoppingRegistration.Dispose();
            applicationStoppedRegistration.Dispose();

            if (cancelKeyHandler is not null)
            {
                Console.CancelKeyPress -= cancelKeyHandler;
            }

            if (unhandledExceptionHandler is not null)
            {
                AppDomain.CurrentDomain.UnhandledException -= unhandledExceptionHandler;
            }

            if (unobservedTaskHandler is not null)
            {
                TaskScheduler.UnobservedTaskException -= unobservedTaskHandler;
            }

            ApplicationShutdownCoordinator.Configure(null);

            if (tuiCts is not null)
            {
                tuiCts.Cancel();
            }

            if (hostStarted)
            {
                try
                {
                    TuiManager.FlushPersistedSession();
                }
                catch when (startupFailure is not null)
                {
                }
            }

            if (app is not null && hostStarted)
            {
                try
                {
                    await app.StopAsync();
                }
                catch (OperationCanceledException)
                {
                }
                catch when (startupFailure is not null)
                {
                }
            }

            if (appTask is not null || tuiTask is not null)
            {
                try
                {
                    await Task.WhenAll(appTask ?? Task.CompletedTask, tuiTask ?? Task.CompletedTask);
                }
                catch (OperationCanceledException)
                {
                }
                catch when (startupFailure is not null)
                {
                }
            }

            if (app is not null)
            {
                try
                {
                    await app.DisposeAsync();
                }
                catch when (startupFailure is not null)
                {
                }
            }

            tuiCts?.Dispose();
            diagnosticLoggerProvider?.Dispose();
            diagnosticLog?.Dispose();
        }
    }

    internal static RuntimeDiagnosticLog CreateRuntimeDiagnosticLog()
    {
        try
        {
            return RuntimeDiagnosticLog.CreateDefault();
        }
        catch (Exception ex)
        {
            var fallbackPath = RuntimeDiagnosticLog.GetCandidatePaths()[0];
            Console.Error.WriteLine($"LlamaFleece runtime diagnostics are unavailable: {ex.Message}");
            return RuntimeDiagnosticLog.CreateDisabled(fallbackPath);
        }
    }

    private static InteractionPersistenceService? CreatePersistenceService(ProxyOptions proxyOptions)
    {
        if (!proxyOptions.Persistence.Enabled)
        {
            return null;
        }

        var persistenceService = new InteractionPersistenceService(proxyOptions.Persistence.GetResolvedSessionFilePath());
        try
        {
            persistenceService.PreflightSessionFile();
        }
        catch (Exception ex)
        {
            throw new StartupPersistencePreflightException(persistenceService.SessionFilePath, ex);
        }

        return persistenceService;
    }

    private static void ConfigureHost(
        WebApplicationBuilder builder,
        ProxyOptions proxyOptions,
        UpstreamRequestHeaderInjection upstreamRequestHeaderInjection)
    {
        builder.Services.AddSingleton(proxyOptions);
        builder.Services.AddSingleton(Options.Create(proxyOptions));
        builder.Services.AddSingleton(upstreamRequestHeaderInjection);
        builder.Services.Configure<HostOptions>(options =>
        {
            if (proxyOptions.GetShutdownTimeout() is { } shutdownTimeout)
            {
                options.ShutdownTimeout = shutdownTimeout;
            }
        });

        builder.WebHost.ConfigureKestrel(options =>
        {
            var listenHost = proxyOptions.GetNormalizedListenHost();
            var listenPort = proxyOptions.ListenPort!.Value;

            if (listenHost.Equals(ProxyOptions.DefaultListenHost, StringComparison.OrdinalIgnoreCase))
            {
                options.ListenLocalhost(listenPort);
                return;
            }

            if (!IPAddress.TryParse(listenHost, out var listenAddress))
            {
                throw new InvalidOperationException($"Proxy:ListenHost '{listenHost}' could not be parsed after validation.");
            }

            if (IPAddress.Any.Equals(listenAddress) || IPAddress.IPv6Any.Equals(listenAddress))
            {
                options.ListenAnyIP(listenPort);
                return;
            }

            options.Listen(listenAddress, listenPort);
        });

        var routes = new[]
        {
            new RouteConfig
            {
                RouteId = "api_route",
                ClusterId = "llama_cluster",
                Match = new RouteMatch { Path = "{**catch-all}" }
            }
        };

        var clusters = new[]
        {
            new ClusterConfig
            {
                ClusterId = "llama_cluster",
                Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
                {
                    { "llama_destination", new DestinationConfig { Address = proxyOptions.UpstreamUrl! } }
                }
            }
        };

        builder.Services.AddReverseProxy()
            .LoadFromMemory(routes, clusters)
            .AddTransforms(transformBuilderContext => upstreamRequestHeaderInjection.Apply(transformBuilderContext));

        builder.Services.AddHttpClient<TrackedRequestCoordinator>(client =>
        {
            client.BaseAddress = proxyOptions.GetUpstreamUri();
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        builder.Services.AddSingleton<IInteractionReplayService, InteractionReplayService>();
    }

    private static string FormatShutdownReason(string? reason)
    {
        return string.IsNullOrWhiteSpace(reason) ? "<unknown>" : reason;
    }

    private static bool ShouldWriteDiagnosticLog(string? category, LogLevel level)
    {
        if (level == LogLevel.None)
        {
            return false;
        }

        if (string.Equals(category, "Microsoft.Hosting.Lifetime", StringComparison.Ordinal))
        {
            return level >= LogLevel.Information;
        }

        if (!string.IsNullOrWhiteSpace(category) &&
            (category.StartsWith("Microsoft.", StringComparison.Ordinal)
            || category.StartsWith("System.", StringComparison.Ordinal)
            || category.StartsWith("Yarp.", StringComparison.Ordinal)))
        {
            return level >= LogLevel.Warning;
        }

        return level >= LogLevel.Information;
    }

    private static void RestorePersistedSession(InteractionPersistenceService? interactionPersistenceService)
    {
        if (interactionPersistenceService is null)
        {
            return;
        }

        try
        {
            TuiManager.RestorePersistedSession();
        }
        catch (Exception ex)
        {
            throw new StartupPersistenceRestoreException(interactionPersistenceService.SessionFilePath, ex);
        }
    }

    private static async Task StartHostAsync(WebApplication app, ProxyOptions proxyOptions)
    {
        try
        {
            await app.StartAsync();
        }
        catch (Exception ex) when (StartupFailureReporter.IsLikelyPortBindingFailure(ex))
        {
            throw new StartupPortBindingException(
                $"http://{proxyOptions.GetDisplayListenHost()}:{proxyOptions.ListenPort}",
                ex);
        }
    }

    private static void AppendStartupMessages(
        ProxyOptions proxyOptions,
        UpstreamRequestHeaderInjection upstreamRequestHeaderInjection,
        InteractionPersistenceService? interactionPersistenceService)
    {
        TuiManager.AppendLog($"LlamaFleece Proxy started on http://{proxyOptions.GetDisplayListenHost()}:{proxyOptions.ListenPort}.");
        if (!proxyOptions.IsLoopbackOnlyBinding())
        {
            TuiManager.AppendLog("Security warning: non-loopback binding allows reachable clients to use any configured upstream credentials, and captured interactions can then be replayed, exported, or persisted locally.");
        }

        TuiManager.AppendLog($"Proxying to {proxyOptions.UpstreamUrl!}.");
        if (upstreamRequestHeaderInjection.Count > 0)
        {
            TuiManager.AppendLog($"Applying {upstreamRequestHeaderInjection.Count} configured upstream request header(s).");
        }

        if (interactionPersistenceService is not null)
        {
            TuiManager.AppendLog($"Session persistence path: {interactionPersistenceService.SessionFilePath}.");
        }
    }
}


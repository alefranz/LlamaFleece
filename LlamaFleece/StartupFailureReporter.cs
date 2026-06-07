using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;

internal static class StartupFailureReporter
{
    public const int FailureExitCode = 1;

    public static bool IsLikelyPortBindingFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is StartupPortBindingException)
        {
            return true;
        }

        if (FindSocketException(exception) is { SocketErrorCode: SocketError.AddressAlreadyInUse or SocketError.AccessDenied or SocketError.AddressNotAvailable })
        {
            return true;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is IOException ioException &&
                ioException.Message.Contains("Failed to bind", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (current.GetType().Name.Equals("AddressInUseException", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static int WriteFriendlyMessage(Exception exception, TextWriter? writer = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        (writer ?? Console.Error).Write(Format(exception));
        return FailureExitCode;
    }

    internal static string Format(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var builder = new StringBuilder();
        builder.AppendLine("LlamaFleece could not start.");
        builder.AppendLine();

        switch (exception)
        {
            case OptionsValidationException optionsValidationException:
                builder.AppendLine("Configuration is invalid.");
                foreach (var failure in optionsValidationException.Failures)
                {
                    builder.Append("- ");
                    builder.AppendLine(failure);
                }

                builder.AppendLine();
                builder.AppendLine("Fix the values above in appsettings.json, appsettings.*.json, or your Proxy__... environment variables, then start LlamaFleece again.");
                break;

            case StartupPortBindingException portBindingException:
                builder.AppendLine($"The proxy could not listen on {portBindingException.ListenUri}.");
                builder.Append("- ");
                builder.AppendLine(DescribePortBindingCause(portBindingException));
                builder.AppendLine();
                builder.AppendLine("Stop the conflicting process or change Proxy:ListenHost / Proxy:ListenPort, then start LlamaFleece again.");
                break;

            case StartupPersistenceRestoreException persistenceRestoreException:
                builder.AppendLine($"The persisted session could not be restored from {persistenceRestoreException.SessionFilePath}.");
                builder.Append("- ");
                builder.AppendLine(GetRelevantMessage(persistenceRestoreException.InnerException) ?? persistenceRestoreException.Message);
                builder.AppendLine();
                builder.AppendLine("Move, delete, or repair that file, or set Proxy:Persistence:Enabled=false, then start LlamaFleece again.");
                break;

            case StartupPersistencePreflightException persistencePreflightException:
                builder.AppendLine($"The persisted session file could not be prepared at {persistencePreflightException.SessionFilePath}.");
                builder.Append("- ");
                builder.AppendLine(GetRelevantMessage(persistencePreflightException.InnerException) ?? persistencePreflightException.Message);
                builder.AppendLine();
                builder.AppendLine("Make sure the parent directory is writable and no other process is locking that file, or set Proxy:Persistence:Enabled=false, then start LlamaFleece again.");
                break;

            case StartupTerminalInitializationException terminalInitializationException:
                builder.AppendLine("The interactive terminal UI could not be initialized.");
                builder.Append("- ");
                builder.AppendLine(GetRelevantMessage(terminalInitializationException.InnerException) ?? terminalInitializationException.Message);
                builder.AppendLine();
                builder.AppendLine("Run LlamaFleece in a real interactive terminal with ANSI output and keyboard input, then start it again.");
                break;

            default:
                builder.AppendLine("Startup failed unexpectedly.");
                builder.Append("- ");
                builder.AppendLine(GetRelevantMessage(exception) ?? "No additional error details were available.");
                builder.AppendLine();
                builder.AppendLine("Run LlamaFleece under a debugger if you need a full stack trace.");
                break;
        }

        return builder.ToString();
    }

    private static string DescribePortBindingCause(StartupPortBindingException exception)
    {
        if (FindSocketException(exception) is { SocketErrorCode: SocketError.AddressAlreadyInUse })
        {
            return "Another process is already using that address.";
        }

        if (FindSocketException(exception) is { SocketErrorCode: SocketError.AccessDenied })
        {
            return "The operating system rejected that bind. This can happen when the address is reserved or blocked by permissions.";
        }

        if (FindSocketException(exception) is { SocketErrorCode: SocketError.AddressNotAvailable })
        {
            return "That listen address is not available on this machine.";
        }

        return GetRelevantMessage(exception.InnerException) ?? "Kestrel rejected the listen address during startup.";
    }

    private static SocketException? FindSocketException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException socketException)
            {
                return socketException;
            }
        }

        return null;
    }

    private static string? GetRelevantMessage(Exception? exception)
    {
        string? message = null;
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message))
            {
                message = current.Message;
            }
        }

        return NormalizeMessage(message);
    }

    private static string? NormalizeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        return message
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }
}

internal sealed class StartupPortBindingException : Exception
{
    public StartupPortBindingException(string listenUri, Exception innerException)
        : base($"The proxy could not listen on {listenUri}.", innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listenUri);
        ArgumentNullException.ThrowIfNull(innerException);

        ListenUri = listenUri;
    }

    public string ListenUri { get; }
}

internal sealed class StartupPersistenceRestoreException : Exception
{
    public StartupPersistenceRestoreException(string sessionFilePath, Exception innerException)
        : base($"The persisted session at {sessionFilePath} could not be restored.", innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionFilePath);
        ArgumentNullException.ThrowIfNull(innerException);

        SessionFilePath = sessionFilePath;
    }

    public string SessionFilePath { get; }
}

internal sealed class StartupPersistencePreflightException : Exception
{
    public StartupPersistencePreflightException(string sessionFilePath, Exception innerException)
        : base($"The persisted session file at {sessionFilePath} could not be prepared for writes.", innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionFilePath);
        ArgumentNullException.ThrowIfNull(innerException);

        SessionFilePath = sessionFilePath;
    }

    public string SessionFilePath { get; }
}

internal sealed class StartupTerminalInitializationException : Exception
{
    public StartupTerminalInitializationException(Exception innerException)
        : base("The interactive terminal UI could not be initialized.", innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }
}
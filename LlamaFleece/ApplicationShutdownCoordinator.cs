internal static class ApplicationShutdownCoordinator
{
    private static Action<string?>? _requestShutdown;
    private static int _shutdownRequested;
    private static string? _shutdownReason;

    public static bool IsShutdownRequested => Volatile.Read(ref _shutdownRequested) != 0;

    public static string? ShutdownReason => Volatile.Read(ref _shutdownReason);

    public static void Configure(Action<string?>? requestShutdown)
    {
        Interlocked.Exchange(ref _requestShutdown, requestShutdown);
        Volatile.Write(ref _shutdownReason, null);
        Volatile.Write(ref _shutdownRequested, 0);
    }

    public static void RequestShutdown(string? reason = null)
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _shutdownReason, NormalizeReason(reason));
        var requestShutdown = Interlocked.CompareExchange(ref _requestShutdown, null, null);
        requestShutdown?.Invoke(Volatile.Read(ref _shutdownReason));
    }

    internal static void ResetForTests()
    {
        Configure(null);
    }

    private static string? NormalizeReason(string? reason)
    {
        return string.IsNullOrWhiteSpace(reason)
            ? null
            : reason.Trim();
    }
}
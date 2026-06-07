internal static class StartupEntryPoint
{
    public static Task<int> RunAsync(string[] args)
    {
        return Program.Main(args);
    }
}
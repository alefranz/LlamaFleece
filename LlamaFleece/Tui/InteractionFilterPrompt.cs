using Spectre.Console;

internal static class InteractionFilterPrompt
{
    private static Func<string, string?>? _promptOverrideForTests;

    public static string? Prompt(string currentQuery)
    {
        var promptOverride = _promptOverrideForTests;
        if (promptOverride is not null)
        {
            return promptOverride(currentQuery);
        }

        var prompt = new TextPrompt<string>(
                "\n[bold cyan]Search / Filter Interactions[/]\n" +
                "[dim]Plain terms search model, endpoint, status, and finish.[/]\n" +
                "[dim]Filters: model=, endpoint=, status=, finish=, prompt>=, completion>=, total>=, after=, before=. Blank clears.[/]\n" +
                "[dim]Examples: qwen endpoint=/v1/responses status=200 total>=100 after=10:30[/]\n" +
                "> ")
            .AllowEmpty();

        if (!string.IsNullOrWhiteSpace(currentQuery))
        {
            prompt.DefaultValue(currentQuery);
        }

        try
        {
            return AnsiConsole.Prompt(prompt);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    internal static void SetPromptOverrideForTests(Func<string, string?>? promptOverride)
    {
        _promptOverrideForTests = promptOverride;
    }
}
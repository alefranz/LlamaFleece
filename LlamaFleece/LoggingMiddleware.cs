using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Spectre.Console;

public class LoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TrackedRequestCoordinator _trackedRequestCoordinator;

    public LoggingMiddleware(RequestDelegate next, TrackedRequestCoordinator trackedRequestCoordinator)
    {
        _next = next;
        _trackedRequestCoordinator = trackedRequestCoordinator;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var startTime = DateTime.UtcNow;
        bool shouldLog = IsTrackedInteractionRequest(context.Request);
        var requestEnvelope = CreateRequestEnvelope(context.Request);
        var displayTarget = requestEnvelope.GetRedactedDisplayTarget();

        // Keep a rolling request/response history so the log view is useful when opened after traffic has already occurred.
        TuiManager.AppendLog($">>> {context.Request.Method,-7} {displayTarget}");

        if (shouldLog)
        {
            TuiManager.NewSession();
            TuiManager.SetLatestRequestEnvelope(requestEnvelope);
            TuiManager.AppendInput($"[bold magenta]>[/] [bold white]New {context.Request.Method} Request[/] to [cyan]{Markup.Escape(displayTarget)}[/]");

            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var reqJson = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;

            var trackedRequest = TrackedRequestPayload.Create(requestEnvelope, reqJson);

            TuiManager.AppendRawInput(reqJson);

            AppendStructuredInputPreview(displayTarget, reqJson);

            await _trackedRequestCoordinator.ProxyAsync(context, trackedRequest);
        }
        else
        {
            await _next(context);
        }

        long? contentLen = context.Response.ContentLength;
        string sizeStr = contentLen.HasValue && contentLen.Value > 0 ? $"{contentLen.Value} B" : "";
        var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
        TuiManager.AppendLog($"<<< {context.Response.StatusCode,-6} {sizeStr,-10} {elapsed,8:F0} ms  {context.Request.Method,-7} {displayTarget}");
    }

    internal static void AppendStructuredInputPreview(string requestPath, string reqJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(reqJson);
            if (TryGetString(doc.RootElement, "model", out var model))
            {
                TuiManager.CurrentModel = model;
            }

            AppendInputPreview(doc.RootElement);
        }
        catch (Exception ex)
        {
            var failureMessage = BuildStructuredPreviewFailureMessage(ex);
            TuiManager.AppendInputMessage("yellow", "preview", failureMessage);
            TuiManager.AppendLog($"!!! Structured preview unavailable for {requestPath}: {failureMessage}");
        }
    }

    private static InteractionRequestEnvelope CreateRequestEnvelope(HttpRequest request)
    {
        return new InteractionRequestEnvelope
        {
            Method = request.Method,
            Path = request.Path.HasValue ? request.Path.Value! : "/",
            QueryString = request.QueryString.HasValue ? request.QueryString.Value! : string.Empty,
            ContentType = string.IsNullOrWhiteSpace(request.ContentType) ? "application/json" : request.ContentType
        };
    }

    private static string BuildStructuredPreviewFailureMessage(Exception ex)
    {
        var detail = ex.Message.Replace(Environment.NewLine, " ").Trim();

        if (ex is JsonException)
        {
            return string.IsNullOrWhiteSpace(detail)
                ? "Structured preview unavailable: request body is not valid JSON. Showing redacted raw request body only."
                : $"Structured preview unavailable: request body is not valid JSON ({ex.GetType().Name}: {detail}). Showing redacted raw request body only.";
        }

        return string.IsNullOrWhiteSpace(detail)
            ? $"Structured preview unavailable: structured extraction failed ({ex.GetType().Name}). Showing redacted raw request body only."
            : $"Structured preview unavailable: structured extraction failed ({ex.GetType().Name}: {detail}). Showing redacted raw request body only.";
    }

    private static bool IsTrackedInteractionRequest(HttpRequest request)
    {
        if (!request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return InteractionEndpointClassifier.IsTracked(request.Path.Value);
    }

    private static void AppendInputPreview(JsonElement root)
    {
        if (TryGetString(root, "instructions", out var instructions))
        {
            TuiManager.AppendInputMessage("yellow", "instructions", instructions);
        }

        if (root.TryGetProperty("system", out var systemProp) && TryExtractText(systemProp, out var systemText))
        {
            TuiManager.AppendInputMessage("yellow", "system", systemText);
        }

        if (root.TryGetProperty("messages", out var messagesProp) && messagesProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var message in messagesProp.EnumerateArray())
            {
                AppendStructuredInputItem(message);
            }

            return;
        }

        if (root.TryGetProperty("prompt", out var promptProp) && promptProp.ValueKind == JsonValueKind.String)
        {
            TuiManager.AppendInputMessage("green", "prompt", promptProp.GetString() ?? string.Empty);
            return;
        }

        if (root.TryGetProperty("input", out var inputProp))
        {
            AppendResponsesInput(inputProp);
        }
    }

    private static void AppendResponsesInput(JsonElement input)
    {
        switch (input.ValueKind)
        {
            case JsonValueKind.String:
                TuiManager.AppendInputMessage("green", "input", input.GetString() ?? string.Empty);
                return;

            case JsonValueKind.Array:
                foreach (var item in input.EnumerateArray())
                {
                    AppendStructuredInputItem(item);
                }

                return;

            case JsonValueKind.Object:
                AppendStructuredInputItem(input);
                return;
        }
    }

    private static void AppendStructuredInputItem(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            TuiManager.AppendInputMessage("green", "input", item.GetString() ?? string.Empty);
            return;
        }

        if (item.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var label = TryGetString(item, "role", out var role)
            ? role
            : TryGetString(item, "type", out var type)
                ? type
                : "input";

        if (TryExtractDisplayText(item, out var text))
        {
            TuiManager.AppendInputMessage(GetInputColor(label), label, text);
            return;
        }

        TuiManager.AppendInputMessage(GetInputColor(label), label, item.GetRawText());
    }

    private static bool TryExtractDisplayText(JsonElement item, out string text)
    {
        if (item.TryGetProperty("content", out var content) && TryExtractText(content, out text))
        {
            return true;
        }

        if (item.TryGetProperty("output", out var output) && TryExtractText(output, out text))
        {
            return true;
        }

        if (item.TryGetProperty("input", out var input) && TryExtractText(input, out text))
        {
            return true;
        }

        if (TryGetString(item, "text", out text))
        {
            return true;
        }

        if (item.TryGetProperty("arguments", out var arguments) && TryExtractText(arguments, out text))
        {
            return true;
        }

        text = string.Empty;
        return false;
    }

    private static bool TryExtractText(JsonElement element, out string text)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                text = element.GetString() ?? string.Empty;
                return true;

            case JsonValueKind.Array:
                var parts = new List<string>();
                foreach (var child in element.EnumerateArray())
                {
                    if (TryExtractText(child, out var part) && !string.IsNullOrWhiteSpace(part))
                    {
                        parts.Add(part);
                    }
                }

                text = string.Join("\n", parts);
                return parts.Count > 0;

            case JsonValueKind.Object:
                if (TryGetString(element, "text", out text))
                {
                    return true;
                }

                if (element.TryGetProperty("content", out var nestedContent) && TryExtractText(nestedContent, out text))
                {
                    return true;
                }

                if (element.TryGetProperty("output", out var nestedOutput) && TryExtractText(nestedOutput, out text))
                {
                    return true;
                }

                if (element.TryGetProperty("input", out var nestedInput) && TryExtractText(nestedInput, out text))
                {
                    return true;
                }

                break;
        }

        text = string.Empty;
        return false;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string GetInputColor(string label)
    {
        return label switch
        {
            "user" or "prompt" or "input" => "green",
            "system" or "instructions" => "yellow",
            "tool" or "tool_output" or "tool_result" or "tool_use" or "function_call_output" or "custom_tool_call_output" => "magenta",
            _ => "blue"
        };
    }
}

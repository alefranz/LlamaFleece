using System.Text.Json.Nodes;
using Xunit;

public class InteractionSecretRedactorTests
{
    [Fact]
    public void RedactRequestBody_RedactsSensitiveJsonFieldsAndNestedStringSecrets()
    {
        const string requestJson = """
        {
          "model": "gpt-test",
          "authorization": "Bearer sk-request-secret-token",
          "messages": [
            { "role": "user", "content": "Use sk-request-secret-token in the follow-up." }
          ],
          "tool": {
            "api_key": "request-tool-secret"
          }
        }
        """;

        var redacted = InteractionSecretRedactor.RedactRequestBody(requestJson);
        var json = JsonNode.Parse(redacted)!.AsObject();
        var messages = json["messages"]!.AsArray();

        Assert.Equal("gpt-test", json["model"]!.GetValue<string>());
        Assert.Equal(InteractionSecretRedactor.RedactionToken, json["authorization"]!.GetValue<string>());
        Assert.Equal("Use REDACTED in the follow-up.", messages[0]!["content"]!.GetValue<string>());
        Assert.Equal(InteractionSecretRedactor.RedactionToken, json["tool"]!["api_key"]!.GetValue<string>());
    }

    [Fact]
    public void RedactResponseBody_RedactsSensitiveSseDataLinesAndPreservesDoneMarker()
    {
        const string responseBody = "event: message\n" +
                                    "data: {\"choices\":[{\"delta\":{\"content\":\"Response token sk-response-secret-token\"}}]}\n" +
                                    "data: [DONE]\n";

        var redacted = InteractionSecretRedactor.RedactResponseBody(responseBody);
        var lines = redacted.Split('\n', StringSplitOptions.None);

        Assert.Equal("event: message", lines[0]);
        Assert.Equal("data: {\"choices\":[{\"delta\":{\"content\":\"Response token REDACTED\"}}]}", lines[1]);
        Assert.Equal("data: [DONE]", lines[2]);
        Assert.DoesNotContain("sk-response-secret-token", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactQueryString_RedactsSensitiveParametersAndPreservesOthers()
    {
        var redacted = InteractionSecretRedactor.RedactQueryString(
            "?api-key=query-secret-token&api-version=2026-05-01&cursor=page-2");

        Assert.Equal(
            "?api-key=REDACTED&api-version=2026-05-01&cursor=page-2",
            redacted);
    }
}
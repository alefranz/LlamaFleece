using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class InteractionSecretRedactor
{
    public const string RedactionToken = "REDACTED";

    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization",
        "proxyauthorization",
        "apikey",
        "xapikey",
        "accesstoken",
        "refreshtoken",
        "idtoken",
        "bearertoken",
        "sessiontoken",
        "clientsecret",
        "secret",
        "password",
        "passwd",
        "pwd",
        "signature",
        "sig",
        "privatekey",
        "clientassertion",
        "assertion",
        "credential",
        "credentials",
        "cookie",
        "setcookie"
    };

    private static readonly JsonSerializerOptions CompactJsonOptions = new() { WriteIndented = false };
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    private static readonly Regex AuthorizationSchemeRegex = new(
        @"\b(?<scheme>Bearer|Basic)\s+(?<token>[A-Za-z0-9\-._~+/]+=*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex LabeledSecretRegex = new(
        @"(?<prefix>\b(?:authorization|proxy-authorization|api[-_ ]?key|x-api-key|access[-_ ]?token|refresh[-_ ]?token|id[-_ ]?token|bearer[-_ ]?token|session[-_ ]?token|client[-_ ]?secret|password|passwd|pwd|signature|sig|private[-_ ]?key)\b\s*[:=]\s*)(?<quote>[""']?)(?<value>[^""'\s,;&]+)(\k<quote>)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex JwtRegex = new(
        @"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AnthropicKeyRegex = new(
        @"\bsk-ant-[A-Za-z0-9_-]{10,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OpenAiKeyRegex = new(
        @"\bsk-[A-Za-z0-9_-]{10,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GitHubTokenRegex = new(
        @"\bgh[pousr]_[A-Za-z0-9]{20,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string RedactText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var candidate = TryRedactJsonString(value, preferIndented: value.Contains('\n'), out var redactedJson)
            ? redactedJson
            : value;

        return RedactPlainText(candidate);
    }

    public static string RedactRequestBody(string? value)
    {
        return RedactText(value);
    }

    public static string RedactResponseBody(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var lineStart = 0;

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '\n')
            {
                continue;
            }

            builder.Append(RedactResponseLine(value[lineStart..index]));
            builder.Append('\n');
            lineStart = index + 1;
        }

        if (lineStart < value.Length)
        {
            builder.Append(RedactResponseLine(value[lineStart..]));
        }

        return builder.ToString();
    }

    public static string RedactQueryString(string? queryString)
    {
        if (string.IsNullOrWhiteSpace(queryString))
        {
            return string.Empty;
        }

        var hasPrefix = queryString[0] == '?';
        var trimmed = hasPrefix ? queryString[1..] : queryString;
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var segments = trimmed.Split('&');
        var changed = false;

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.Length == 0)
            {
                continue;
            }

            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            var encodedKey = segment[..separatorIndex];
            var encodedValue = segment[(separatorIndex + 1)..];
            var decodedKey = SafeUrlDecode(encodedKey);
            var decodedValue = SafeUrlDecode(encodedValue);

            var redactedValue = IsSensitiveName(decodedKey)
                ? RedactionToken
                : RedactPlainText(decodedValue);

            if (string.Equals(decodedValue, redactedValue, StringComparison.Ordinal))
            {
                continue;
            }

            segments[i] = encodedKey + "=" + redactedValue;
            changed = true;
        }

        if (!changed)
        {
            return queryString;
        }

        return (hasPrefix ? "?" : string.Empty) + string.Join("&", segments);
    }

    public static bool IsSensitiveName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return SensitiveNames.Contains(NormalizeName(value));
    }

    private static string RedactResponseLine(string line)
    {
        if (!line.StartsWith("data: ", StringComparison.Ordinal))
        {
            return RedactPlainText(line);
        }

        var payload = line[6..];
        if (string.Equals(payload, "[DONE]", StringComparison.Ordinal))
        {
            return line;
        }

        var redactedPayload = TryRedactJsonString(payload, preferIndented: false, out var redactedJson)
            ? redactedJson
            : RedactPlainText(payload);

        return string.Equals(payload, redactedPayload, StringComparison.Ordinal)
            ? line
            : "data: " + redactedPayload;
    }

    private static string RedactPlainText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var redacted = LabeledSecretRegex.Replace(value, match =>
        {
            var prefix = match.Groups["prefix"].Value;
            var quote = match.Groups["quote"].Value;
            return prefix + quote + RedactionToken + quote;
        });

        redacted = AuthorizationSchemeRegex.Replace(redacted, match => match.Groups["scheme"].Value + " " + RedactionToken);
        redacted = JwtRegex.Replace(redacted, RedactionToken);
        redacted = AnthropicKeyRegex.Replace(redacted, RedactionToken);
        redacted = OpenAiKeyRegex.Replace(redacted, RedactionToken);
        redacted = GitHubTokenRegex.Replace(redacted, RedactionToken);

        return redacted;
    }

    private static bool TryRedactJsonString(string value, bool preferIndented, out string redacted)
    {
        redacted = string.Empty;
        if (!LooksLikeJson(value))
        {
            return false;
        }

        try
        {
            var node = JsonNode.Parse(value);
            if (node is null)
            {
                return false;
            }

            var changed = false;
            RedactJsonNode(node, ref changed);
            if (!changed)
            {
                return false;
            }

            redacted = node.ToJsonString(preferIndented ? IndentedJsonOptions : CompactJsonOptions);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RedactJsonNode(JsonNode? node, ref bool changed)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj.ToList())
                {
                    if (IsSensitiveName(property.Key))
                    {
                        obj[property.Key] = RedactionToken;
                        changed = true;
                        continue;
                    }

                    RedactJsonNode(property.Value, ref changed);
                }

                return;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    RedactJsonNode(array[i], ref changed);
                }

                return;

            case JsonValue jsonValue when jsonValue.TryGetValue<string>(out var stringValue):
                var redactedValue = RedactText(stringValue);
                if (!string.Equals(stringValue, redactedValue, StringComparison.Ordinal))
                {
                    jsonValue.ReplaceWith(JsonValue.Create(redactedValue));
                    changed = true;
                }

                return;
        }
    }

    private static bool LooksLikeJson(string value)
    {
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            return c == '{' || c == '[';
        }

        return false;
    }

    private static string NormalizeName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    private static string SafeUrlDecode(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        try
        {
            return Uri.UnescapeDataString(value.Replace("+", " ", StringComparison.Ordinal));
        }
        catch
        {
            return value;
        }
    }
}
using System.Globalization;
using System.Text;

internal enum InteractionNumericField
{
    StatusCode,
    PromptTokens,
    CompletionTokens,
    TotalTokens
}

internal enum InteractionNumericComparison
{
    Equal,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}

internal sealed record class InteractionNumericPredicate(
    InteractionNumericField Field,
    InteractionNumericComparison Comparison,
    int Value)
{
    public bool Matches(int? candidate)
    {
        if (!candidate.HasValue)
        {
            return false;
        }

        return Comparison switch
        {
            InteractionNumericComparison.Equal => candidate.Value == Value,
            InteractionNumericComparison.GreaterThan => candidate.Value > Value,
            InteractionNumericComparison.GreaterThanOrEqual => candidate.Value >= Value,
            InteractionNumericComparison.LessThan => candidate.Value < Value,
            InteractionNumericComparison.LessThanOrEqual => candidate.Value <= Value,
            _ => false
        };
    }
}

internal sealed record class InteractionFilter
{
    public static InteractionFilter None { get; } = new();

    public string QueryText { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> TextTerms { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ModelTerms { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> EndpointTerms { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FinishReasonTerms { get; init; } = Array.Empty<string>();
    public IReadOnlyList<InteractionNumericPredicate> NumericPredicates { get; init; } = Array.Empty<InteractionNumericPredicate>();
    public DateTime? StartedAfterUtc { get; init; }
    public DateTime? StartedBeforeUtc { get; init; }

    public bool IsActive => !string.IsNullOrWhiteSpace(QueryText);
}

internal sealed class InteractionFilterParseException : Exception
{
    public InteractionFilterParseException(string message)
        : base(message)
    {
    }
}

internal sealed class InteractionFilterService
{
    private static readonly string[] NumericOperators = [">=", "<=", ">", "<", "="];

    public InteractionFilter Parse(string? queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return InteractionFilter.None;
        }

        var tokens = Tokenize(queryText);
        if (tokens.Count == 0)
        {
            return InteractionFilter.None;
        }

        var textTerms = new List<string>();
        var modelTerms = new List<string>();
        var endpointTerms = new List<string>();
        var finishReasonTerms = new List<string>();
        var numericPredicates = new List<InteractionNumericPredicate>();
        DateTime? startedAfterUtc = null;
        DateTime? startedBeforeUtc = null;

        foreach (var token in tokens)
        {
            if (!TryParseKeyedToken(token, out var key, out var op, out var value))
            {
                textTerms.Add(token);
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InteractionFilterParseException($"Filter '{token}' is missing a value.");
            }

            switch (key)
            {
                case "model":
                    EnsureTextOperator(key, op);
                    modelTerms.Add(value);
                    break;

                case "endpoint":
                case "path":
                    EnsureTextOperator(key, op);
                    endpointTerms.Add(value);
                    break;

                case "finish":
                case "reason":
                    EnsureTextOperator(key, op);
                    finishReasonTerms.Add(value);
                    break;

                case "status":
                case "code":
                    numericPredicates.Add(new InteractionNumericPredicate(
                        InteractionNumericField.StatusCode,
                        ParseComparison(op),
                        ParseInt32(key, value)));
                    break;

                case "prompt":
                    numericPredicates.Add(new InteractionNumericPredicate(
                        InteractionNumericField.PromptTokens,
                        ParseComparison(op),
                        ParseInt32(key, value)));
                    break;

                case "completion":
                case "output":
                    numericPredicates.Add(new InteractionNumericPredicate(
                        InteractionNumericField.CompletionTokens,
                        ParseComparison(op),
                        ParseInt32(key, value)));
                    break;

                case "total":
                    numericPredicates.Add(new InteractionNumericPredicate(
                        InteractionNumericField.TotalTokens,
                        ParseComparison(op),
                        ParseInt32(key, value)));
                    break;

                case "after":
                case "since":
                    EnsureTextOperator(key, op);
                    startedAfterUtc = ParseDateTimeUtc(key, value);
                    break;

                case "before":
                case "until":
                    EnsureTextOperator(key, op);
                    startedBeforeUtc = ParseDateTimeUtc(key, value);
                    break;

                default:
                    throw new InteractionFilterParseException(
                        $"Unsupported filter key '{key}'. Use plain text terms or keys like model=, endpoint=, status=, finish=, prompt>=, completion>=, total>=, after=, before=.");
            }
        }

        if (startedAfterUtc.HasValue && startedBeforeUtc.HasValue && startedAfterUtc > startedBeforeUtc)
        {
            throw new InteractionFilterParseException("The after= time must be earlier than or equal to before=.");
        }

        return new InteractionFilter
        {
            QueryText = queryText.Trim(),
            Summary = queryText.Trim(),
            TextTerms = textTerms,
            ModelTerms = modelTerms,
            EndpointTerms = endpointTerms,
            FinishReasonTerms = finishReasonTerms,
            NumericPredicates = numericPredicates,
            StartedAfterUtc = startedAfterUtc,
            StartedBeforeUtc = startedBeforeUtc
        };
    }

    public IReadOnlyList<int> GetMatchingIndices(IReadOnlyList<Interaction> interactions, InteractionFilter filter)
    {
        ArgumentNullException.ThrowIfNull(interactions);
        ArgumentNullException.ThrowIfNull(filter);

        var matches = new List<int>(interactions.Count);

        for (var index = 0; index < interactions.Count; index++)
        {
            if (Matches(interactions[index], filter))
            {
                matches.Add(index);
            }
        }

        return matches;
    }

    public bool Matches(Interaction interaction, InteractionFilter filter)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(filter);

        if (!filter.IsActive)
        {
            return true;
        }

        var endpoint = interaction.RequestEnvelope?.GetDisplayTarget()
            ?? interaction.RequestEnvelope?.Path
            ?? string.Empty;
        var statusCodeText = interaction.ResponseStatusCode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

        foreach (var textTerm in filter.TextTerms)
        {
            if (!ContainsIgnoreCase(interaction.Model, textTerm) &&
                !ContainsIgnoreCase(endpoint, textTerm) &&
                !ContainsIgnoreCase(statusCodeText, textTerm) &&
                !MatchesOptionalText(interaction.FinishReason, textTerm))
            {
                return false;
            }
        }

        foreach (var modelTerm in filter.ModelTerms)
        {
            if (!ContainsIgnoreCase(interaction.Model, modelTerm))
            {
                return false;
            }
        }

        foreach (var endpointTerm in filter.EndpointTerms)
        {
            if (!ContainsIgnoreCase(endpoint, endpointTerm))
            {
                return false;
            }
        }

        foreach (var finishReasonTerm in filter.FinishReasonTerms)
        {
            if (!MatchesOptionalText(interaction.FinishReason, finishReasonTerm))
            {
                return false;
            }
        }

        foreach (var numericPredicate in filter.NumericPredicates)
        {
            if (!numericPredicate.Matches(GetNumericFieldValue(interaction, numericPredicate.Field)))
            {
                return false;
            }
        }

        var startedAtUtc = NormalizeUtc(interaction.StartTime);
        if (filter.StartedAfterUtc.HasValue && startedAtUtc < filter.StartedAfterUtc.Value)
        {
            return false;
        }

        if (filter.StartedBeforeUtc.HasValue && startedAtUtc > filter.StartedBeforeUtc.Value)
        {
            return false;
        }

        return true;
    }

    private static bool TryParseKeyedToken(string token, out string key, out string op, out string value)
    {
        foreach (var candidate in NumericOperators)
        {
            var separatorIndex = token.IndexOf(candidate, StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            key = token[..separatorIndex].Trim().ToLowerInvariant();
            op = candidate;
            value = token[(separatorIndex + candidate.Length)..].Trim();
            return true;
        }

        key = string.Empty;
        op = string.Empty;
        value = string.Empty;
        return false;
    }

    private static List<string> Tokenize(string queryText)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var c in queryText)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (inQuotes)
        {
            throw new InteractionFilterParseException("Unterminated quoted filter value.");
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static void EnsureTextOperator(string key, string op)
    {
        if (op == "=")
        {
            return;
        }

        throw new InteractionFilterParseException($"Filter key '{key}' only supports '='.");
    }

    private static InteractionNumericComparison ParseComparison(string op)
    {
        return op switch
        {
            "=" => InteractionNumericComparison.Equal,
            ">" => InteractionNumericComparison.GreaterThan,
            ">=" => InteractionNumericComparison.GreaterThanOrEqual,
            "<" => InteractionNumericComparison.LessThan,
            "<=" => InteractionNumericComparison.LessThanOrEqual,
            _ => throw new InteractionFilterParseException($"Unsupported comparison operator '{op}'.")
        };
    }

    private static int ParseInt32(string key, string value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new InteractionFilterParseException($"Filter key '{key}' requires an integer value.");
    }

    private static DateTime ParseDateTimeUtc(string key, string value)
    {
        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out var parsedOffset))
        {
            return parsedOffset.UtcDateTime;
        }

        if (DateTime.TryParse(
                value,
                CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out var parsedDateTime))
        {
            return NormalizeUtc(parsedDateTime);
        }

        throw new InteractionFilterParseException(
            $"Filter key '{key}' requires a date or time like 2026-05-19T10:30 or 10:30.");
    }

    private static int? GetNumericFieldValue(Interaction interaction, InteractionNumericField field)
    {
        return field switch
        {
            InteractionNumericField.StatusCode => interaction.ResponseStatusCode,
            InteractionNumericField.PromptTokens => interaction.PromptTokens,
            InteractionNumericField.CompletionTokens => interaction.CompletionTokens,
            InteractionNumericField.TotalTokens => interaction.TotalTokens,
            _ => null
        };
    }

    private static bool ContainsIgnoreCase(string? value, string term)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesOptionalText(string? value, string term)
    {
        if (IsEmptyValueTerm(term))
        {
            return string.IsNullOrWhiteSpace(value);
        }

        return ContainsIgnoreCase(value, term);
    }

    private static bool IsEmptyValueTerm(string term)
    {
        return term.Equals("none", StringComparison.OrdinalIgnoreCase) ||
               term.Equals("n/a", StringComparison.OrdinalIgnoreCase) ||
               term.Equals("unknown", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime(),
            _ => value.ToUniversalTime()
        };
    }
}
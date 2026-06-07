public sealed class InteractionRequestEnvelope
{
    public string Method { get; set; } = "POST";

    public string Path { get; set; } = "/";

    public string QueryString { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/json";

    public string GetNormalizedQueryString()
    {
        if (string.IsNullOrWhiteSpace(QueryString))
        {
            return string.Empty;
        }

        return QueryString[0] == '?'
            ? QueryString
            : $"?{QueryString}";
    }

    public string GetDisplayTarget()
    {
        var path = string.IsNullOrWhiteSpace(Path) ? "/" : Path;
        return path + GetNormalizedQueryString();
    }

    public string GetRedactedDisplayTarget()
    {
        var path = string.IsNullOrWhiteSpace(Path) ? "/" : Path;
        return path + InteractionSecretRedactor.RedactQueryString(GetNormalizedQueryString());
    }

    public InteractionRequestEnvelope Clone()
    {
        return new InteractionRequestEnvelope
        {
            Method = Method,
            Path = Path,
            QueryString = QueryString,
            ContentType = ContentType
        };
    }

    public InteractionRequestEnvelope CloneRedacted()
    {
        return new InteractionRequestEnvelope
        {
            Method = Method,
            Path = Path,
            QueryString = InteractionSecretRedactor.RedactQueryString(QueryString),
            ContentType = ContentType
        };
    }
}
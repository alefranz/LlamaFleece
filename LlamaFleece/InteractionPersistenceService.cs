using System.Text;
using System.Text.Json;

internal sealed class InteractionPersistenceService
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly JsonSerializerOptions _jsonOptions = InteractionExportService.CreateJsonOptions();
    private readonly TimeSpan _minimumPersistInterval;
    private DateTime _lastPersistedAtUtc = DateTime.MinValue;

    public InteractionPersistenceService(string sessionFilePath, TimeSpan? minimumPersistInterval = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionFilePath);

        SessionFilePath = Path.GetFullPath(sessionFilePath);
        _minimumPersistInterval = minimumPersistInterval ?? TimeSpan.FromSeconds(1);
    }

    public string SessionFilePath { get; }

    public void PreflightSessionFile()
    {
        EnsureParentDirectoryExists();

        var directoryPath = Path.GetDirectoryName(SessionFilePath) ?? Directory.GetCurrentDirectory();
        var probeFilePath = Path.Combine(
            directoryPath,
            $"{Path.GetFileName(SessionFilePath)}.{Path.GetRandomFileName()}.preflight");

        using (var probeStream = new FileStream(
            probeFilePath,
            new FileStreamOptions
            {
                Access = FileAccess.Write,
                Mode = FileMode.CreateNew,
                Share = FileShare.None,
                Options = FileOptions.DeleteOnClose
            }))
        {
            probeStream.WriteByte(0);
        }

        if (!File.Exists(SessionFilePath))
        {
            return;
        }

        using var sessionFileStream = new FileStream(
            SessionFilePath,
            new FileStreamOptions
            {
                Access = FileAccess.ReadWrite,
                Mode = FileMode.Open,
                Share = FileShare.None
            });
    }

    public InteractionPersistenceSaveResult SaveSession(InteractionExportSessionSnapshot session, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(session);

        var persistedAtUtc = DateTime.UtcNow;
        if (!force &&
            _minimumPersistInterval > TimeSpan.Zero &&
            _lastPersistedAtUtc != DateTime.MinValue &&
            persistedAtUtc - _lastPersistedAtUtc < _minimumPersistInterval)
        {
            return InteractionPersistenceSaveResult.CreateSkipped(SessionFilePath);
        }

        var document = new InteractionPersistenceDocument
        {
            PersistedAtUtc = persistedAtUtc,
            Session = session
        };

        EnsureParentDirectoryExists();

        var tempFilePath = SessionFilePath + ".tmp";
        File.WriteAllText(tempFilePath, JsonSerializer.Serialize(document, _jsonOptions), Utf8NoBom);
        File.Move(tempFilePath, SessionFilePath, overwrite: true);

        _lastPersistedAtUtc = persistedAtUtc;
        return InteractionPersistenceSaveResult.CreateWritten(SessionFilePath, persistedAtUtc);
    }

    public InteractionPersistenceLoadResult LoadSession()
    {
        if (!File.Exists(SessionFilePath))
        {
            return InteractionPersistenceLoadResult.NotFound(SessionFilePath);
        }

        var json = File.ReadAllText(SessionFilePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("Persisted session file is empty.");
        }

        var document = JsonSerializer.Deserialize<InteractionPersistenceDocument>(json, _jsonOptions)
            ?? throw new InvalidDataException("Persisted session file did not contain a valid session document.");

        if (!string.Equals(document.Type, InteractionPersistenceDocument.DocumentType, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Persisted session file type '{document.Type}' is not supported.");
        }

        if (document.Version != InteractionPersistenceDocument.CurrentVersion)
        {
            throw new InvalidDataException($"Persisted session file version '{document.Version}' is not supported.");
        }

        _lastPersistedAtUtc = document.PersistedAtUtc;

        return InteractionPersistenceLoadResult.Restored(
            SessionFilePath,
            document.PersistedAtUtc,
            InteractionExportService.RestoreSession(document.Session));
    }

    private void EnsureParentDirectoryExists()
    {
        var directoryPath = Path.GetDirectoryName(SessionFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
    }
}

internal sealed record class InteractionPersistenceSaveResult(bool Persisted, bool Skipped, string FilePath, DateTime? PersistedAtUtc)
{
    public static InteractionPersistenceSaveResult CreateSkipped(string filePath)
    {
        return new InteractionPersistenceSaveResult(Persisted: false, Skipped: true, filePath, PersistedAtUtc: null);
    }

    public static InteractionPersistenceSaveResult CreateWritten(string filePath, DateTime persistedAtUtc)
    {
        return new InteractionPersistenceSaveResult(Persisted: true, Skipped: false, filePath, persistedAtUtc);
    }
}

internal sealed record class InteractionPersistenceLoadResult(string FilePath, DateTime? PersistedAtUtc, RestoredInteractionSession? Session)
{
    public bool Found => Session is not null;

    public static InteractionPersistenceLoadResult NotFound(string filePath)
    {
        return new InteractionPersistenceLoadResult(filePath, PersistedAtUtc: null, Session: null);
    }

    public static InteractionPersistenceLoadResult Restored(string filePath, DateTime persistedAtUtc, RestoredInteractionSession session)
    {
        return new InteractionPersistenceLoadResult(filePath, persistedAtUtc, session);
    }
}

internal sealed record class InteractionPersistenceDocument
{
    public const string DocumentType = "session-state";
    public const int CurrentVersion = 1;

    public string Type { get; init; } = DocumentType;
    public int Version { get; init; } = CurrentVersion;
    public DateTime PersistedAtUtc { get; init; }
    public InteractionExportSessionSnapshot Session { get; init; } = new();
}
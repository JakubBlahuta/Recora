namespace Recora.Core.Paths;

/// <summary>
/// All per-user paths under %LOCALAPPDATA%\Recora.
/// </summary>
public sealed class RecoraPaths
{
    public const string RootFolderName = "Recora";

    public string Root { get; }
    public string Data { get; }
    public string Recordings { get; }
    public string Cache { get; }
    public string CatalogDb { get; }
    public string Config { get; }
    public string SettingsFile { get; }
    public string Secrets { get; }
    public string Logs { get; }

    public RecoraPaths(string? localAppData = null)
    {
        var baseDir = localAppData
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(baseDir))
        {
            baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share");
        }

        Root = Path.Combine(baseDir, RootFolderName);
        Data = Path.Combine(Root, "data");
        Recordings = Path.Combine(Data, "recordings");
        Cache = Path.Combine(Data, "cache");
        CatalogDb = Path.Combine(Data, "catalog.db");
        Config = Path.Combine(Root, "config");
        SettingsFile = Path.Combine(Config, "settings.json");
        Secrets = Path.Combine(Config, "secrets");
        Logs = Path.Combine(Root, "logs");
    }

    public string MeetingDirectory(string meetingId) =>
        Path.Combine(Recordings, SanitizeMeetingId(meetingId));

    public static string SanitizeMeetingId(string meetingId)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            meetingId = meetingId.Replace(c, '_');
        return meetingId;
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Recordings);
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(Config);
        Directory.CreateDirectory(Secrets);
        Directory.CreateDirectory(Logs);
    }
}

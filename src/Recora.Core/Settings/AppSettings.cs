using System.Text.Json;
using System.Text.Json.Serialization;
using Recora.Core.Models;

namespace Recora.Core.Settings;

public sealed class AppSettings
{
    public string UiLanguage { get; set; } = "cs";
    public bool AutomationEnabled { get; set; } = true;
    public int ChunkDurationMinutes { get; set; } = 20;
    public string QualityProfile { get; set; } = nameof(Models.QualityProfile.SpeechFirst);
    public bool FirstRunCompleted { get; set; }
    public bool ConsentAccepted { get; set; }
    public CaptureSettings Capture { get; set; } = new();
    public CloudSettings Cloud { get; set; } = new();
    public FormaSettings Forma { get; set; } = new();
    public DetectionSettings Detection { get; set; } = new();
    public bool MitigateDoubleVoice { get; set; } = true;

    public QualityProfile GetQualityProfile() =>
        Enum.TryParse<QualityProfile>(QualityProfile, ignoreCase: true, out var p)
            ? p
            : Models.QualityProfile.SpeechFirst;

    public UiLanguage GetUiLanguage() => UiLanguage.ToLowerInvariant() switch
    {
        "sk" or "sk-sk" => Models.UiLanguage.Sk,
        "en" or "en-us" or "en-gb" => Models.UiLanguage.En,
        _ => Models.UiLanguage.Cs
    };

    public static AppSettings CreateDefaults(UiLanguage? fromOs = null)
    {
        var lang = fromOs ?? DetectOsLanguage();
        return new AppSettings
        {
            UiLanguage = lang switch
            {
                Models.UiLanguage.Sk => "sk",
                Models.UiLanguage.En => "en",
                _ => "cs"
            }
        };
    }

    public static UiLanguage DetectOsLanguage()
    {
        var name = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return name switch
        {
            "sk" => Models.UiLanguage.Sk,
            "cs" => Models.UiLanguage.Cs,
            _ => Models.UiLanguage.En
        };
    }
}

public sealed class CaptureSettings
{
    public string Mode { get; set; } = "window";
    public string Audio { get; set; } = "teamsPlusMic";
    public bool IncludeMicrophone { get; set; } = true;
    public string MicrophoneDeviceId { get; set; } = "default";
}

public sealed class CloudSettings
{
    public string Provider { get; set; } = "onedrive";
    public string FolderPath { get; set; } = "Recordings";
    public bool AutoUpload { get; set; } = true;
    public bool OAuthCompleted { get; set; }
}

public sealed class FormaSettings
{
    public bool Enabled { get; set; }
    public bool AutoUpload { get; set; }
}

public sealed class DetectionSettings
{
    public bool GraphEnabled { get; set; }
    public string MinConfidenceToRecord { get; set; } = "medium";

    public DetectionConfidence GetMinConfidence() =>
        Enum.TryParse<DetectionConfidence>(MinConfidenceToRecord, ignoreCase: true, out var c)
            ? c
            : DetectionConfidence.Medium;
}

public static class SettingsJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

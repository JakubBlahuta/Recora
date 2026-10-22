using Recora.Core.Models;

namespace Recora.Core.Localization;

/// <summary>
/// Built-in CS/SK/EN strings used by core services and as fallback for WinUI resources.
/// </summary>
public static class StringCatalog
{
    private static readonly Dictionary<string, Dictionary<UiLanguage, string>> Map = new()
    {
        ["App.Name"] = new()
        {
            [UiLanguage.Cs] = "Recora",
            [UiLanguage.Sk] = "Recora",
            [UiLanguage.En] = "Recora"
        },
        ["Tray.Idle"] = new()
        {
            [UiLanguage.Cs] = "Recora — nečinná",
            [UiLanguage.Sk] = "Recora — nečinná",
            [UiLanguage.En] = "Recora — idle"
        },
        ["Tray.Recording"] = new()
        {
            [UiLanguage.Cs] = "Recora — nahrává",
            [UiLanguage.Sk] = "Recora — nahráva",
            [UiLanguage.En] = "Recora — recording"
        },
        ["Tray.Uncertain"] = new()
        {
            [UiLanguage.Cs] = "Recora — nejistá detekce",
            [UiLanguage.Sk] = "Recora — neistá detekcia",
            [UiLanguage.En] = "Recora — uncertain detection"
        },
        ["Tray.ForceRecord"] = new()
        {
            [UiLanguage.Cs] = "Vynutit nahrávání",
            [UiLanguage.Sk] = "Vynútiť nahrávanie",
            [UiLanguage.En] = "Force record"
        },
        ["Tray.Stop"] = new()
        {
            [UiLanguage.Cs] = "Zastavit",
            [UiLanguage.Sk] = "Zastaviť",
            [UiLanguage.En] = "Stop"
        },
        ["Tray.OpenLibrary"] = new()
        {
            [UiLanguage.Cs] = "Otevřít knihovnu",
            [UiLanguage.Sk] = "Otvoriť knižnicu",
            [UiLanguage.En] = "Open library"
        },
        ["Tray.Exit"] = new()
        {
            [UiLanguage.Cs] = "Ukončit",
            [UiLanguage.Sk] = "Ukončiť",
            [UiLanguage.En] = "Exit"
        },
        ["Wizard.Title"] = new()
        {
            [UiLanguage.Cs] = "Vítejte v Recora",
            [UiLanguage.Sk] = "Vitajte v Recora",
            [UiLanguage.En] = "Welcome to Recora"
        },
        ["Wizard.Language"] = new()
        {
            [UiLanguage.Cs] = "Jazyk rozhraní",
            [UiLanguage.Sk] = "Jazyk rozhrania",
            [UiLanguage.En] = "Interface language"
        },
        ["Wizard.Consent"] = new()
        {
            [UiLanguage.Cs] = "Beru na vědomí, že za souhlas účastníků nahrávky odpovídám já.",
            [UiLanguage.Sk] = "Beriem na vedomie, že za súhlas účastníkov nahrávky zodpovedám ja.",
            [UiLanguage.En] = "I understand I am responsible for obtaining participant consent to record."
        },
        ["Wizard.OneDrive"] = new()
        {
            [UiLanguage.Cs] = "Přihlásit OneDrive a zvolit složku (návrh: Recordings)",
            [UiLanguage.Sk] = "Prihlásiť OneDrive a zvoliť priečinok (návrh: Recordings)",
            [UiLanguage.En] = "Sign in to OneDrive and choose a folder (suggested: Recordings)"
        },
        ["Wizard.AutoUpload"] = new()
        {
            [UiLanguage.Cs] = "Automaticky nahrávat hotové nahrávky do OneDrive",
            [UiLanguage.Sk] = "Automaticky nahrávať hotové nahrávky do OneDrive",
            [UiLanguage.En] = "Automatically upload finished recordings to OneDrive"
        },
        ["Wizard.Finish"] = new()
        {
            [UiLanguage.Cs] = "Hotovo — lokální knihovna je primární",
            [UiLanguage.Sk] = "Hotovo — lokálna knižnica je primárna",
            [UiLanguage.En] = "Done — local library is primary"
        },
        ["Library.Title"] = new()
        {
            [UiLanguage.Cs] = "Knihovna nahrávek",
            [UiLanguage.Sk] = "Knižnica nahrávok",
            [UiLanguage.En] = "Recording library"
        },
        ["Library.Empty"] = new()
        {
            [UiLanguage.Cs] = "Zatím žádné nahrávky. Recora začne automaticky při Teams hovoru.",
            [UiLanguage.Sk] = "Zatiaľ žiadne nahrávky. Recora začne automaticky pri Teams hovore.",
            [UiLanguage.En] = "No recordings yet. Recora starts automatically on a Teams call."
        },
        ["Library.Search"] = new()
        {
            [UiLanguage.Cs] = "Hledat…",
            [UiLanguage.Sk] = "Hľadať…",
            [UiLanguage.En] = "Search…"
        },
        ["Library.TranscriptPlaceholder"] = new()
        {
            [UiLanguage.Cs] = "Přepis — připraveno pro AI",
            [UiLanguage.Sk] = "Prepis — pripravené pre AI",
            [UiLanguage.En] = "Transcript — ready for AI"
        },
        ["Settings.Title"] = new()
        {
            [UiLanguage.Cs] = "Nastavení",
            [UiLanguage.Sk] = "Nastavenia",
            [UiLanguage.En] = "Settings"
        },
        ["Settings.Mic"] = new()
        {
            [UiLanguage.Cs] = "Mikrofon v nahrávce",
            [UiLanguage.Sk] = "Mikrofón v nahrávke",
            [UiLanguage.En] = "Include microphone"
        },
        ["Settings.MicDevice"] = new()
        {
            [UiLanguage.Cs] = "Mikrofonní zařízení",
            [UiLanguage.Sk] = "Mikrofónové zariadenie",
            [UiLanguage.En] = "Microphone device"
        },
        ["Settings.AutoUpload"] = new()
        {
            [UiLanguage.Cs] = "Automatický upload do OneDrive",
            [UiLanguage.Sk] = "Automatický upload do OneDrive",
            [UiLanguage.En] = "Auto-upload to OneDrive"
        },
        ["Settings.Folder"] = new()
        {
            [UiLanguage.Cs] = "Složka OneDrive",
            [UiLanguage.Sk] = "Priečinok OneDrive",
            [UiLanguage.En] = "OneDrive folder"
        },
        ["Status.Local"] = new()
        {
            [UiLanguage.Cs] = "Lokální",
            [UiLanguage.Sk] = "Lokálne",
            [UiLanguage.En] = "Local"
        },
        ["Status.Uploading"] = new()
        {
            [UiLanguage.Cs] = "Nahrává se…",
            [UiLanguage.Sk] = "Nahráva sa…",
            [UiLanguage.En] = "Uploading…"
        },
        ["Status.Synced"] = new()
        {
            [UiLanguage.Cs] = "V OneDrive",
            [UiLanguage.Sk] = "V OneDrive",
            [UiLanguage.En] = "Synced"
        },
        ["Status.Failed"] = new()
        {
            [UiLanguage.Cs] = "Upload selhal",
            [UiLanguage.Sk] = "Upload zlyhal",
            [UiLanguage.En] = "Upload failed"
        },
        ["Status.Recovered"] = new()
        {
            [UiLanguage.Cs] = "Obnoveno po pádu",
            [UiLanguage.Sk] = "Obnovené po páde",
            [UiLanguage.En] = "Recovered after crash"
        }
    };

    public static string Get(string key, UiLanguage language)
    {
        if (Map.TryGetValue(key, out var byLang) && byLang.TryGetValue(language, out var value))
            return value;
        if (Map.TryGetValue(key, out byLang) && byLang.TryGetValue(UiLanguage.En, out value))
            return value;
        return key;
    }

    public static IEnumerable<string> Keys => Map.Keys;
}

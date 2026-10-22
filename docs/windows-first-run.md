# Recora — lokální běh na Windows (bez Cursor Remote Control)

Průvodce pro vývojáře: stáhnout PR větev, otevřít ve Visual Studio 2022, F5, otestovat force-record a nahlásit chyby. **Nepotřebujete** Cursor team Remote Control ani Azure client ID.

## 1. Checkout PR větve

```powershell
git clone https://github.com/JakubBlahuta/Recora.git
cd Recora
git fetch origin pull/1/head:pr-1
git checkout pr-1
# nebo: git checkout cursor/recora-mvp-scaffold-c642
```

PR: https://github.com/JakubBlahuta/Recora/pull/1

## 2. Visual Studio 2022 — workloady

Ve Visual Studio Installer:

1. **.NET desktop development**
2. **Windows application development** (WinUI / Windows App SDK / MSIX)

Ověřte .NET 8 SDK a Windows 10/11 SDK (≥ 10.0.19041).

## 3. Otevřít a F5

```powershell
start Recora.sln
```

1. Pravý klik na **Recora.App** → **Set as Startup Project**.
2. Konfigurace **Debug**, platforma **x64** (ne Any CPU).
3. Profil spuštění:
   - **Recora.App (unpackaged)** — nastaví `RECORA_FAKE_CAPTURE=1` (bez Teams, falešný capture).
   - **Recora.App (live capture)** — vypne fake; použije WinRT Graphics Capture + WASAPI (Teams okno + mic), pokud jsou dostupné.
4. **F5**.

Aplikace běží unpackaged (bez instalace MSIX).

## 4. První spuštění

1. Průvodce: jazyk (CS/SK/EN) → souhlas → OneDrive **lze přeskočit**.
2. V systémové liště (tray) se objeví ikona Recora.
3. Tray → **Otevřít knihovnu**.

### Kde leží soubory

```
%LOCALAPPDATA%\Recora\
  config\settings.json
  config\secrets\          # tokeny (DPAPI), pokud použijete stub OneDrive
  data\catalog.db
  data\recordings\<meetingId>\
    chunk_000.mp4
    meeting.json
    chunks.jsonl
  logs\tray-YYYYMMDD.log
```

## 5. Test bez schůzky (force record)

### A) Fake režim (doporučeno napoprvé)

Profil **unpackaged** (`RECORA_FAKE_CAPTURE=1`):

1. Tray → **Vynutit nahrávání**.
2. Počkejte 5–10 s.
3. Tray → **Zastavit**.
4. Knihovna ukáže nahrávku; na disku vzniknou chunk + sidecar.

### B) Live režim (Teams + mic)

1. Spusťte profil **live capture** (bez `RECORA_FAKE_CAPTURE`).
2. Volitelně spusťte Teams a vstupte do hovoru (nebo force record).
3. V **Nastavení** zvolte mikrofon (skutečný seznam WASAPI zařízení).
4. Force record / auto-detect → Stop.
5. Chunky:
   - s **ffmpeg** na PATH (nebo `RECORA_FFMPEG`) → AAC MP4 (přehratelné, audio-first);
   - bez ffmpeg → minimální přehratelný MP4 stub + varování v UI / `ErrorMessage` meetingu.

```powershell
# Volitelné — lepší encode
winget install FFmpeg
# nebo: $env:RECORA_FFMPEG = "C:\path\to\ffmpeg.exe"
```

### Ukázková data

Nastavení → **Ukázková data v knihovně** → **Načíst ukázková data teď**  
(nebo `"demoDataEnabled": true` / `$env:RECORA_DEMO_DATA=1`).

## 6. OneDrive (volitelné)

Bez `RECORA_MSAL_CLIENT_ID` zůstává Recora **local-only** (stub token / nic se neodesílá). Azure app registration až později — viz README.

## 7. Jak nahlásit bug

Do issue / komentáře u PR #1 přiložte:

1. Profil (fake vs live), OS (Win10/11), x64.
2. Postup (force record / Teams call).
3. Výřez logu: `%LOCALAPPDATA%\Recora\logs\tray-*.log` (bez obsahu hovoru).
4. Zda existují `meeting.json` / `chunk_*.mp4` a zda jdou otevřít v přehrávači.
5. Screenshot tray / knihovny / případné chybové hlášky encode.

## 8. Co ještě není hotové

- Plný video dump z Graphics Capture → H.264 (zatím audio-first / stub).
- Per-process WASAPI loopback jen na `ms-teams.exe` (teď system loopback).
- MSAL interaktivní OneDrive (čeká na client id).
- Podepsaný MSIX sideload (`Recora.Package` ve VS → Publish).

Podrobnosti buildu: [`windows-build.md`](./windows-build.md).

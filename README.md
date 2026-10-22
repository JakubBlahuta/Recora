# Recora

**Recora** automatically records Microsoft Teams calls on Windows (window + Teams audio + microphone), stores AI-ready chunked recordings locally under `%LOCALAPPDATA%\Recora`, and can sync finished meetings to your personal OneDrive.

Internal code name: TCR. UI languages: **CS / SK / EN**.

> This repository was scaffolded on a Linux cloud agent. **WinUI 3 / MSIX builds require Windows** (Visual Studio 2022 + Windows App SDK). Core libraries and unit tests build on Linux/CI.

## Solution layout

```
Recora.sln
src/
  Recora.Core/        Paths, settings, models, agent state machine, i18n catalog
  Recora.Catalog/     SQLite meeting/chunk/upload catalog
  Recora.Detection/   Teams L1 (process/window) + L2 (title heuristics CS/SK/EN)
  Recora.Capture/     WinRT Graphics Capture + WASAPI Teams loopback + mic mixer
  Recora.Encoding/    Chunk rotation, SpeechFirst profile, sidecar JSON, recovery
  Recora.Cloud/       OneDrive OAuth scaffolding (DPAPI) + upload queue
  Recora.App/         WinUI 3 shell: tray, wizard, library, settings
  Recora.Package/     Per-user MSIX + StartupTask manifest
tests/
  Recora.Core.Tests/  Unit tests (run on Linux + Windows)
```

Runtime data (created on first launch):

```
%LOCALAPPDATA%\Recora\
  data\catalog.db
  data\recordings\<meetingId>\chunk_*.mp4, meeting.json, chunks.jsonl
  config\settings.json
  config\secrets\          # DPAPI-protected tokens
  logs\
```

## Defaults (locked)

| Setting | Default |
|--------|---------|
| Audio | Teams WASAPI + **microphone ON** |
| Mic device | System default (picker in Settings) |
| Chunk length | 20 minutes (15–30) |
| Quality | SpeechFirst (720p / low fps / AAC speech) |
| Cloud | Personal OneDrive folder `Recordings`, **auto-upload ON** after wizard |
| Packaging | Per-user MSIX + `windows.startupTask` |

## Build on Windows

### Prerequisites

1. Windows 10 22H2+ or Windows 11  
2. [Visual Studio 2022](https://visualstudio.microsoft.com/) with workloads:
   - .NET desktop development
   - Windows application development (WinUI / Windows App SDK)
3. .NET 8 SDK  
4. (Optional) Azure App Registration — public client — for real OneDrive OAuth

### Open & run

```powershell
git clone https://github.com/JakubBlahuta/Recora.git
cd Recora
# Open Recora.sln in Visual Studio 2022
# Set Recora.App as startup project → F5 (unpackaged WinUI)
```

Or CLI (Windows only for the app):

```powershell
dotnet restore Recora.sln
dotnet build src\Recora.App\Recora.App.csproj -c Debug
dotnet run --project src\Recora.App\Recora.App.csproj
```

### Libraries + tests (any OS)

```bash
dotnet test tests/Recora.Core.Tests/Recora.Core.Tests.csproj -c Release
```

### MSIX (per-user, no admin)

1. Open `Recora.sln` in Visual Studio  
2. Right-click `Recora.Package` → **Publish** → **Create App Packages…**  
3. Sideload or AppInstaller distribution  
4. Manifest includes `windows.startupTask` (`RecoraStartup`) for login autostart  

Users install into their profile; no UAC for normal install/update.

## OneDrive OAuth

Set a public-client application ID (user-provided secret):

```powershell
$env:RECORA_MSAL_CLIENT_ID = "<azure-app-client-id>"
```

Scopes (planned): `offline_access`, `Files.ReadWrite`, `User.Read`.

Without a client ID, the app uses an **offline stub token** so wizard + upload queue can be exercised locally. Tokens are stored under `config\secrets` via DPAPI.

## What works vs stubbed

| Area | Status |
|------|--------|
| Settings / paths / i18n catalog | **Works** |
| SQLite catalog + search | **Works** (tested) |
| Teams title heuristics L2 | **Works** (tested); L1 EnumWindows on Windows |
| Recording agent state machine | **Works** |
| Chunk sidecars + crash orphan finalize | **Works** (placeholder MP4 bytes) |
| OneDrive upload queue | **Works** with stub token; Graph PUT ready when MSAL wired |
| WinUI wizard / library / settings | **Source complete** — build on Windows |
| Tray (H.NotifyIcon) | **Source complete** — Windows |
| WinRT Graphics Capture + real WASAPI PCM | **Armed / spike stubs** — bind COM on Windows |
| Media Foundation / FFmpeg encode | **Stub** — replace placeholder chunk writer |
| MSAL interactive browser | **Blocked** on missing `RECORA_MSAL_CLIENT_ID` |

## CI notes

GitHub Actions on Linux builds **libraries + tests only** (see `.github/workflows/ci.yml`).  
Add a Windows runner job later to `dotnet build src/Recora.App` and produce MSIX artifacts.

## Smoke checklist (Windows)

- [ ] Fresh install / F5 → first-run wizard (CS/SK/EN)  
- [ ] Consent + OneDrive folder + auto-upload ON  
- [ ] Join Teams call → tray shows Recording (or Uncertain + Force record)  
- [ ] Leave call → meeting appears in library with sidecar files  
- [ ] Settings: toggle mic, change device, disable auto-upload  
- [ ] Kill process mid-record → restart recovers orphan session  

## License / brand

Product name **Recora**. Not affiliated with Microsoft Teams. Users are responsible for obtaining participant consent to record.

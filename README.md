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
dotnet test Recora.Libraries.slnf -c Release
```

- First run (Czech): [`docs/windows-first-run.md`](docs/windows-first-run.md)  
- VS workloads & MSIX: [`docs/windows-build.md`](docs/windows-build.md)

### MSIX (per-user, no admin)

1. Open `Recora.sln` in Visual Studio  
2. Right-click `Recora.Package` → **Publish** → **Create App Packages…**  
3. Sideload or AppInstaller distribution  
4. Manifest includes `windows.startupTask` (`RecoraStartup`) for login autostart  

Users install into their profile; no UAC for normal install/update.

### Demo without Teams (Windows or CI-style)

```powershell
$env:RECORA_FAKE_CAPTURE = "1"
# then run Recora.App — Force record uses FakeCaptureSession + deterministic chunks
```

## OneDrive OAuth

### Default (no Azure app yet)

Without `RECORA_MSAL_CLIENT_ID`, or with `cloud.useMsal: false` in settings, **Sign in** stores a **stub token** (DPAPI under `config\secrets`). The upload queue runs end-to-end locally; nothing is sent to Microsoft.

### Azure app registration (for real Graph upload)

1. Open [Azure Portal → App registrations](https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade) → **New registration**.  
2. Name: `Recora` (or similar).  
3. Supported account types: **Personal Microsoft accounts only** (or “Accounts in any org + personal” if you need both).  
4. Redirect URI: platform **Mobile and desktop applications** → check  
   `https://login.microsoftonline.com/common/oauth2/nativeclient`  
   and/or add `http://localhost` (MSAL loopback).  
5. Register → copy **Application (client) ID**.  
6. **Authentication** → enable **Allow public client flows** = **Yes** (device/public client).  
7. **API permissions** → Microsoft Graph **delegated**:  
   - `User.Read`  
   - `Files.ReadWrite`  
   - (MSAL also requests `offline_access` for refresh)  
   No admin consent required for personal OneDrive.  
8. On the PC:

```powershell
$env:RECORA_MSAL_CLIENT_ID = "<application-client-id>"
```

9. In Recora Settings / `settings.json`, set `"cloud": { "useMsal": true, ... }`.  
10. MSAL interactive browser wiring is still a follow-up on Windows (`NotImplementedException` until implemented); the flag + env gate is ready.

Tokens always stay in `%LOCALAPPDATA%\Recora\config\secrets` via DPAPI — never in the repo.

## What works vs stubbed

| Area | Status |
|------|--------|
| Settings / paths / i18n / viewmodels | **Works** (tested) |
| SQLite catalog + search + upload jobs | **Works** (tested) |
| Teams L2 heuristics + debounce | **Works** (tested); L1 EnumWindows on Windows |
| Recording agent + crash recovery reindex | **Works** (tested) |
| Fake capture + deterministic chunks | **Works** for E2E demos/tests |
| OneDrive queue + retry/backoff | **Works** with stub token |
| WinUI wizard / library / settings | **Source complete** — build on Windows |
| Tray (H.NotifyIcon) | **Source complete** — Windows |
| WinRT Graphics Capture + WASAPI (Windows TFM) | **Implemented** behind `RECORA_WINDOWS` / live profile; fake on Linux |
| Mic device picker | **WASAPI MMDevice list** on Windows; simulated off-Windows |
| Chunk encode | **ffmpeg AAC** if on PATH / `RECORA_FFMPEG`; else **minimal playable MP4 stub** + UI warning |
| MSAL interactive browser | **Gated** — needs `RECORA_MSAL_CLIENT_ID` + `useMsal` + Windows MSAL wire-up |

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

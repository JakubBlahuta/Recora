# Building Recora.App on Windows (VS 2022)

Czech first-run walkthrough: [`windows-first-run.md`](./windows-first-run.md).

## 1. Install Visual Studio 2022

Download [Visual Studio 2022](https://visualstudio.microsoft.com/) (Community is fine).

### Required workloads

In the Visual Studio Installer, select:

| Workload | Why |
|----------|-----|
| **.NET desktop development** | .NET 8 SDK, desktop tooling |
| **Windows application development** | WinUI 3, Windows App SDK, MSIX packaging |

### Individual components (verify)

- .NET 8.0 SDK  
- Windows 10/11 SDK (10.0.19041 or newer)  
- Windows App SDK C# Templates  
- Single-project MSIX Packaging Tools (optional; we also ship `Recora.Package`)

## 2. Clone & open

```powershell
git clone https://github.com/JakubBlahuta/Recora.git
cd Recora
start Recora.sln
```

Set **Recora.App** as the startup project.

## 3. Run unpackaged (daily debug)

1. Ensure `Recora.App` → Properties → Debug → (or project file) `WindowsPackageType` is `None` (already set).  
2. Press **F5**.  
3. First launch opens the wizard (language → consent → OneDrive → finish).  
4. Tray icon appears; open Library from the tray menu.

### Fake capture mode (no Teams required)

```powershell
$env:RECORA_FAKE_CAPTURE = "1"
dotnet run --project src\Recora.App\Recora.App.csproj
```

Uses `FakeCaptureSession` + deterministic chunk encoder so Force record produces catalog entries and sidecars without WinRT.

## 4. MSIX sideload

1. Right-click **Recora.Package** → **Publish** → **Create App Packages…**  
2. Sideloading → x64 → create.  
3. Install the `.msix` (per-user; no admin).  
4. Startup: Settings → Apps → Startup → **Recora** (`RecoraStartup` task from the manifest).

## 5. Libraries-only (any OS / CI)

```bash
dotnet test Recora.Libraries.slnf -c Release
```

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| Missing Windows App SDK | Install workload “Windows application development”, restart VS |
| `NETSDK1100` / RID errors | Build for `x64` (Configuration Manager) |
| Tray icon missing | Check notification area overflow; run elevated? (should not need elevation) |
| OneDrive sign-in throws | Unset `cloud.useMsal` or omit `RECORA_MSAL_CLIENT_ID` to use stub; see README Azure steps |

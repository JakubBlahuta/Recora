# Windows build & MSIX notes

## Visual Studio

1. Install VS 2022 with **.NET desktop development** and **Windows application development**.
2. Open `Recora.sln`.
3. Startup project: `Recora.App` (unpackaged for daily debug) or deploy via `Recora.Package`.

## Capture / encode on device

Spike checklist (Phase 0):

1. Bind `GraphicsCaptureItem` to Teams call HWND (see `GraphicsCaptureHost`).
2. WASAPI process loopback for `ms-teams.exe`; fallback system loopback.
3. Mic capture via selected `microphoneDeviceId`; mix to one AAC track.
4. Replace `ActiveEncoding.CreatePlaceholderMp4Hint` with Media Foundation or bundled FFmpeg SpeechFirst encode.
5. Verify A/V sync with shared monotonic clock; write gaps into sidecar.

## Packaging

- `Package.appxmanifest` declares `windows.startupTask` id `RecoraStartup`.
- Capability `runFullTrust` + `microphone` + `internetClient`.
- Sign with a test certificate for sideload; Store signing later.

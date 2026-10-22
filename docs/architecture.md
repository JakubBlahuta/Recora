# Recora architecture (code map)

Matches locked product architecture v2.1.

```
Tray agent (Recora.App)
  ├─ Teams detector (Recora.Detection)     L1 process/window + L2 heuristics
  ├─ Capture engine (Recora.Capture)       Graphics Capture + WASAPI Teams + mic
  ├─ Encoding pipeline (Recora.Encoding)   chunk rotate, sidecar, SpeechFirst
  ├─ Catalog DB (Recora.Catalog)           SQLite source of truth
  ├─ UI shell (Recora.App Views)           wizard, library, settings, player
  └─ OneDrive sync (Recora.Cloud)          OAuth + upload queue (local first)
```

**Out of MVP:** Autodesk Forma adapter, Graph calendar, dual audio tracks, portable ZIP.

See Project store docs for the full locked decisions and MVP phases.

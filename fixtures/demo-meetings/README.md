# Demo meeting fixtures

Runtime seeding is implemented in `Recora.Core.Demo.DemoDataSeeder` (writes under `%LOCALAPPDATA%\Recora\data\recordings\`).

Enable via:

- Settings → Demo data toggle / „Načíst ukázková data teď“
- `settings.json` → `"demoDataEnabled": true`
- `$env:RECORA_DEMO_DATA = "1"`

Fixture meeting ids: `demo-q3-sync`, `demo-standup`.

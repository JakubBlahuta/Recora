# Recora — první spuštění na Windows

Krátký návod: otevřít řešení ve Visual Studio 2022, spustit F5 a ověřit tray + knihovnu **bez** Teams hovoru.

## 1. Co nainstalovat

1. [Visual Studio 2022](https://visualstudio.microsoft.com/) (Community stačí).
2. Ve Visual Studio Installer zaškrtněte workloady:
   - **.NET desktop development**
   - **Windows application development** (WinUI / Windows App SDK / MSIX)
3. .NET 8 SDK (součást workloadu).

Podrobnosti: [`windows-build.md`](./windows-build.md).

## 2. Otevřít a spustit

```powershell
git clone https://github.com/JakubBlahuta/Recora.git
cd Recora
start Recora.sln
```

1. V Solution Exploreru klikněte pravým na **Recora.App** → **Set as Startup Project**.
2. Nahoře zvolte konfiguraci **Debug** a platformu **x64** (ne Any CPU / x86).
3. Profil spuštění: **Recora.App (unpackaged)** — už nastavuje `RECORA_FAKE_CAPTURE=1`.
4. Stiskněte **F5**.

Aplikace běží **bez instalace MSIX** (unpackaged). Balíček je projekt `Recora.Package` (až budete chtít sideload).

## 3. Co uvidíte

1. **Průvodce (first-run):** jazyk (CS/SK/EN) → souhlas → OneDrive (volitelné) → Hotovo.  
   - OneDrive **nemusíte** přihlašovat. Bez Azure client ID stačí lokální režim (stub token / přeskočit).
2. Po dokončení průvodce běží **ikona v systémové liště (tray)**.
3. Z tray: **Otevřít knihovnu** / **Vynutit nahrávání** / **Zastavit** / **Ukončit**.

Data: `%LOCALAPPDATA%\Recora\` (`config\settings.json`, `data\catalog.db`, `data\recordings\`, `logs\`).

## 4. Test bez schůzky (force record)

Profil **unpackaged** už má fake capture. Postup:

1. Tray → **Vynutit nahrávání** (Force record).  
2. Počkejte pár sekund (tray stav „nahrává“).  
3. Tray → **Zastavit**.  
4. Otevřete knihovnu — měla by přibýt nahrávka se sidecar soubory (`meeting.json`, `chunks.jsonl`, `chunk_000.mp4`).

Chunky jsou zatím **deterministické placeholdery** (ne přehrávatelný H.264) — ověřují pipeline a katalog, ne obraz/zvuk.

### Ukázková data v knihovně

- **Nastavení** → zapněte **Ukázková data v knihovně** → **Načíst ukázková data teď**,  
  nebo nastavte `"demoDataEnabled": true` v `settings.json`,  
  nebo `$env:RECORA_DEMO_DATA = "1"` před spuštěním.  
- Do katalogu se idempotentně přidají 2 demo meetingy (`demo-q3-sync`, `demo-standup`).

## 5. OneDrive (volitelné)

- **Lokální režim (výchozí):** bez `RECORA_MSAL_CLIENT_ID` — nic se neodesílá do Microsoftu; fronta může běžet se stub tokenem.  
- **Skutečný Graph upload:** až budete mít Azure App Registration (viz README) — nastavte client ID a `cloud.useMsal`. Teď to **není potřeba** pro F5 a knihovnu.

## 6. Když něco selže

| Problém | Co zkusit |
|---------|-----------|
| Chybí Windows App SDK | Doinstalovat workload „Windows application development“, restart VS |
| Build jen pro x86 / chyby RID | Solution platform **x64** |
| Prázdná knihovna | Force record, nebo načíst demo data v Nastavení |
| Tray nevidíte | Šipka „skryté ikony“ v oznamovací oblasti |
| F5 nefunguje | Startup = Recora.App, unpackaged profil |

## 7. Co ještě nejde na tomto PC (očekávané)

- Ostrý WinRT Graphics Capture + WASAPI Teams/mic → až na Windows se zapnutým Teams.  
- Skutečný H.264/AAC encode (Media Foundation / FFmpeg).  
- Podepsaný MSIX sideload (projekt `Recora.Package` je připravený ve VS).

Až projde F5 + force record + knihovna, jste hotovi s „open in VS and run“. Další krok je live capture/encode na Windows.

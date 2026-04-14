# WhisperWriter – Copilot Instructions

Tento soubor popisuje aktuální stav projektu **WhisperWriter** a slouží jako kontext pro GitHub Copilot / AI chat, aby mohl plynule navázat na předchozí práci.

---

## 1. Co aplikace dělá

WhisperWriter je minimalistická WPF aplikace pro Windows – **lokální hlasový přepis textu (push-to-talk)**. Inspirace: Wispr Flow.

1. Uživatel drží klávesovou zkratku (výchozí: Levý Ctrl + Levý Win).
2. Aplikace nahrává hlas z mikrofonu.
3. Po puštění kláves se nahrávka přepíše lokálním Whisper modelem (whisper.cpp přes Whisper.net).
4. Přepsaný text se vloží (`SendInput` Unicode) do okna, které mělo focus před nahráváním.

Žádná data neopustí počítač. GPU akcelerace přes CUDA je automatická.

---

## 2. Prostředí a stack

| Položka | Hodnota |
|---|---|
| Platforma | Windows 10/11, .NET 8, WPF + WinForms (NotifyIcon) |
| Projekt | `D:\llms\whisper-writer\WhisperWriter\WhisperWriter.csproj` |
| GPU | NVIDIA Quadro T2000, 4 GB VRAM, CUDA 13.2, driver 595.71 |
| Whisper.net | 1.7.2 (+ Runtime + Runtime.Cuda) |
| NAudio | 2.2.1 |
| System.Text.Json | 8.0.5 |
| Jazyk kódu | C# 12, nullable enable, implicit usings, file-scoped namespaces |
| Odsazení | TAB (ne mezery) |
| Komentáře | Anglicky |

---

## 3. Struktura projektu

```
D:\llms\whisper-writer\
├── .github\
│   └── copilot-instructions.md       ← tento soubor
└── WhisperWriter\
    ├── WhisperWriter.csproj
    ├── App.xaml                       ← globální WPF resources, styly
    ├── App.xaml.cs                    ← startup, tray ikona, statické služby
    ├── AssemblyInfo.cs
    ├── settings.json                  ← výchozí konfigurace (kopíruje se do bin)
    ├── Models\
    │   ├── AppSettings.cs             ← POCO konfigurace (serializováno do settings.json)
    │   └── TranscriptionHistory.cs    ← ObservableCollection<TranscriptionEntry>
    ├── Services\
    │   ├── AudioRecorder.cs           ← NAudio, 16 kHz mono WAV, RMS amplitude event
    │   ├── HotkeyService.cs           ← polling GetAsyncKeyState, 20ms interval
    │   ├── SettingsService.cs         ← JSON load/save do BaseDirectory/settings.json
    │   ├── TextInjector.cs            ← SaveFocus() + SendInput Unicode
    │   └── WhisperService.cs          ← WhisperFactory, TranscribeAsync, CUDA
    ├── Views\
    │   ├── MainWindow.xaml/.cs        ← plovoucí widget (pill), PTT logika, VU meter, ETA
    │   ├── HistoryWindow.xaml/.cs     ← seznam přepisů, copy to clipboard
    │   ├── SettingsWindow.xaml/.cs    ← formulář nastavení
    │   └── AboutWindow.xaml/.cs       ← informační okno o aplikaci
    └── models\
        ├── ggml-large-v2.bin          ← 2.95 GB, výchozí model
        └── ggml-medium.bin            ← 1.46 GB, záložní model
```

---

## 4. Klíčové soubory – přehled

### `App.xaml`
- Definuje globální `ResourceDictionary`: barvy (`BgBrush`, `AccentBrush`, `AccentRecordingBrush`, `TextPrimaryBrush`, `TextSecondaryBrush`) a styl `IconButton`.
- Paleta: tmavé poloprůhledné pozadí `#CC1C1C1E`, akcent `#8B8BFF`, červená pro nahrávání `#FF3B30`.
- `ShutdownMode="OnExplicitShutdown"` – aplikace se nezavře zavřením okna.

### `App.xaml.cs`
- Tři statické služby: `App.SettingsService`, `App.History`, `App.WhisperService`.
- Tray ikona (`NotifyIcon` z WinForms) – **pravé tlačítko** otevírá menu:
  ```
  About WhisperWriter
  ─────────────────────
  Transcriptions
  Settings
  ─────────────────────
  Exit
  ```
- Dvojklik na tray ikonu → `Show()` + `Activate()` na `MainWindow`.
- `WhisperService.InitializeAsync(modelPath)` se spustí asynchronně na pozadí při startu.

### `Models/AppSettings.cs`
```csharp
public class AppSettings
{
    public string ModelPath { get; set; } = "models/ggml-large-v2.bin";
    public string Language  { get; set; } = "auto";   // "auto", "cs", "en", ...
    public string Prompt    { get; set; } = "";
    public int    HotkeyModifiers { get; set; } = 0x0002 | 0x0008; // Ctrl + Win
    public int    HistorySize     { get; set; } = 30;
    public double WindowLeft      { get; set; } = -1;  // -1 = default bottom-center
    public double WindowTop       { get; set; } = -1;
}

[Flags]
public enum HotkeyModifiers { None=0, Alt=1, Control=2, Shift=4, Win=8 }
```

### `Models/TranscriptionHistory.cs`
- `TranscriptionEntry`: `Timestamp`, `Text`, `Duration` (TimeSpan – doba přepisu).
- `TranscriptionHistory.Add()`: thread-safe, vkládá na index 0 (nejnovější nahoře), ořezává na `MaxSize`.

### `Services/SettingsService.cs`
- Ukládá do `AppContext.BaseDirectory + "settings.json"` (tj. vedle exe).
- Při chybě deserializace tiše vrátí `new AppSettings()`.

### `Services/AudioRecorder.cs`
- `WaveInEvent`, 16 000 Hz, 16 bit, mono, buffer 50 ms.
- `StartRecording()` / `StopRecording()` – vrátí `byte[]` WAV.
- `AmplitudeChanged` event (float 0–1 RMS) pro VU meter.

### `Services/HotkeyService.cs`
- **Polling** (ne `RegisterHotKey`) – `GetAsyncKeyState` každých 20 ms na background threadu.
- Důvod: `RegisterHotKey` se Win klávesou chová nespolehlivě na Win 10/11.
- Události: `PushToTalkStarted`, `PushToTalkStopped`.
- Aktuálně hardcoded VK: `VK_LCONTROL (0xA2)` + `VK_LWIN (0x5B)`.
- `HotkeyModifiers` bitmaska z `AppSettings` řídí co se kontroluje.

### `Services/TextInjector.cs`
- `SaveFocus()` – uloží `GetForegroundWindow()` handle před tím, než PTT ukradne focus.
- `InjectText(string)` – `SetForegroundWindow` + 100ms sleep + `SendInput` Unicode (každý char jako keydown+keyup s `KEYEVENTF_UNICODE`).

### `Services/WhisperService.cs`
- `InitializeAsync(modelPath)` – načte model z disku (`WhisperFactory.FromPath`). Pokud model chybí, stáhne medium přes `WhisperGgmlDownloader`.
- `TranscribeAsync(byte[] wavBytes, string language, string prompt)`:
  - Vždy nastavuje explicitní jazyk (`"auto"` → fallback `"cs"`) aby se **zabránilo překladu**.
  - **Nikdy nevolá `WithTranslate()`** – whisper.cpp překládá jen pokud je explicitně zapnuto.
  - Builder: `.WithLanguage(effectiveLanguage).WithThreads(processorCount - 2)`.
  - Volitelně `.WithPrompt(prompt)`.
- `StateChanged` event: `(TranscriptionState, string)` → `Loading`, `Transcribing`, `Done`, `Error`.
- **Pozor**: `WithGreedySamplingStrategy()` vrací `IWhisperSamplingStrategyBuilder` (jiný interface než hlavní builder) – nelze za ním řetězit `WithPrompt` ani `Build()`. Nepřidávat do fluent chain.

### `Views/MainWindow.xaml`
- Plovoucí pill widget, `SizeToContent="WidthAndHeight"`, `Topmost="True"`, bez taskbar.
- **Bez tlačítek** – veškerá navigace přes tray menu.
- Layout: `[dot] [StatusLabel] [EtaLabel]` + pod tím `[AmplitudeRow]` (3px bar, skrytý mimo nahrávání).
- Drag: `MouseLeftButtonDown` → `DragMove()`, pozice se ukládá do settings.
- Animace: `PulseAnim` (blikání RecDot při nahrávání), `FadeIn` při zobrazení.

### `Views/MainWindow.xaml.cs`
- **ETA countdown**: po puštění PTT vypočítá `estimatedSeconds = wavBytes.Length / 32000.0 * EtaFactor`.
  - `EtaFactor = 0.35` (empirický koeficient pro large-v2 + Quadro T2000 CUDA) – **může vyžadovat kalibraci**.
  - `DispatcherTimer` 100 ms odečítá a zobrazuje `~Xs` vedle „Transcribing…".
- Flow PTT: `SaveFocus` → `StartRecording` → (uvolnění) → `StopRecording` → `StartEtaCountdown` → `TranscribeAsync` → `StopEtaCountdown` → `InjectText`.

### `Views/HistoryWindow.xaml`
- `ListView` s `ObservableCollection` bindingem na `App.History.Entries`.
- `DataTemplate`: hlavní text jako **`TextBox` IsReadOnly** (selektovatelný myší!), pod tím timestamp + duration.
- Footer: tlačítko „Copy to clipboard" (aktivní jen pokud je vybrán záznam).

### `Views/SettingsWindow.xaml`
- Formulář: `ModelPath` (TextBox), `Language` (ComboBox: auto/cs/en/de/fr/es/pl/sk), `Prompt` (TextBox multiline), hotkey info (readonly), `HistorySize` (Slider 5–100).
- `ShowDialog()` vrátí `true` při Save, `false` při Cancel/X.

### `Views/AboutWindow.xaml`
- Informační okno, `ScrollViewer` s `TextBlock` (inline `Run` elementy).
- Přetahovatelné, zavíracím tlačítkem nebo „Close" v patičce.

---

## 5. Build a spuštění

```powershell
# Build (bez restore, modely jsou velké)
dotnet build "D:\llms\whisper-writer\WhisperWriter\WhisperWriter.csproj" -c Debug --no-restore

# Spuštění
Start-Process "D:\llms\whisper-writer\WhisperWriter\bin\Debug\net8.0-windows\WhisperWriter.exe"
```

> **Důležité**: Při buildu musí být aplikace ukončena – exe soubor je zamčen běžící instancí.

---

## 6. Známé problémy / TODO

### Otevřené úkoly

| Priorita | Popis |
|---|---|
| Medium | **EtaFactor kalibrace** – aktuálně `0.35`, změřit reálný poměr `transcription_time / recording_length` a případně upravit v `MainWindow.xaml.cs:33`. Ideálně by se faktor počítal adaptivně z posledních N přepisů. |
| Medium | **HotkeyService – konfigurovatelné klávesy** – aktuálně jsou VK kódy `VK_LCONTROL` a `VK_LWIN` hardcoded. `AppSettings.HotkeyModifiers` bitmaska se sice ukládá, ale `IsComboHeld()` ignoruje Alt a Shift větve. Nutno doplnit VK lookup tabulku pro Alt (`VK_LMENU = 0xA4`) a Shift (`VK_LSHIFT = 0xA0`). |
| Low | **settings.json přepsání při buildu** – `PreserveNewest` zkopíruje zdrojový `settings.json` do bin pokud je novější, čímž přepíše uživatelská nastavení. Zvážit `CopyToOutputDirectory=Never` a ruční inicializaci při prvním spuštění (SettingsService to již dělá přes `Save()` když soubor neexistuje). |
| Low | **Whisper model download fallback** – pokud výchozí model (`ggml-large-v2.bin`) chybí, `InitializeAsync` stáhne `GgmlType.Medium` (ne Large). Text v kódu říká „Downloading medium model…" – nesedí s výchozím nastavením. |

### Vyřešené problémy (pro kontext)

- **Překlad místo transkripce**: `WithTranslate()` se nesmí volat. Bez explicitního jazyka whisper.cpp může překládat. Opraveno: vždy `WithLanguage(effectiveLanguage)`, `"auto"` → `"cs"`.
- **`WithGreedySamplingStrategy()` API**: tato metoda vrací jiný interface (`IWhisperSamplingStrategyBuilder`) bez `WithPrompt`/`Build`. Nelze řetězit. Aktuálně se nepoužívá.
- **Ambiguous reference `System.Windows` vs `System.Windows.Forms`**: řešeno aliasem `using WpfApp = System.Windows.Application`.

---

## 7. Konvence kódu

- **OOP**, čistý kód, bez zbytečných závislostí.
- Komentáře **anglicky**, odsazení **TAB**.
- File-scoped namespaces (`namespace WhisperWriter.Services;`).
- Async/await všude kde je I/O (Whisper, disk).
- Win32 P/Invoke interop soustředěn do příslušné třídy (`TextInjector`, `HotkeyService`).
- WPF: `Dispatcher.Invoke` pro cross-thread UI update.
- Žádné MVVM framework – přímý code-behind, aplikace je malá.

---

## 8. Náměty na budoucí rozvoj

Nápady zmíněné v průběhu vývoje, zatím neimplementované:

- **Adaptivní EtaFactor** – průměr z posledních přepisů, uložený do settings.
- **Konfigurovatelná zkratka přes UI** – „press a key" dialog v SettingsWindow.
- **Více jazyků v ComboBoxu** – přidat slovenštinu, němčinu atd. (základ v SettingsWindow.xaml je).
- **Export historie** – CSV nebo prostý text.
- **Tmavý/světlý motiv** – základ palety je v App.xaml, stačí přidat druhý ResourceDictionary.
- **Více mikrofonů** – `WaveInEvent.DeviceNumber`, výběr v Settings.
- **Tichá nahrávka ignorování** – prahová hodnota RMS pod které se nahrávka zahodí (anti-click).

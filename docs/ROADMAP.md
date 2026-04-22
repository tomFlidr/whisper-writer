# WhisperWriter – Roadmap

Tento soubor obsahuje plán všech plánovaných změn v aplikaci, seřazený od těch s **největším dopadem na kód** po ty s nejmenším.
Každá hotová položka se přesouvá do sekce **Dokončeno** a zároveň se aktualizuje `copilot-instructions.md`.

> **Pravidlo pro zápis:** Do tohoto souboru se zapisují pouze věci, které byly skutečně prodiskutovány a odsouhlaseny.
> Nic se nevymýšlí. Každá položka popisuje konkrétní problém, proč se řeší, a co přesně se změní.

---

## Fáze 1 - dodělat hrubý refactoring

Zrefaktorovat MainWindow - rozdělit funkcionalitu do více tříd,
aby byla apliakce připravena na větší změny v dalších fázích 
(nové transkripční engine, sjednocení sekundárních oken).

## Fáze 2 – Sjednocení sekundárních oken do jednoho tabbed okna

> Druhý největší dopad na kód – kompletně přestrukturuje složku `Views/`,
> odstraní tři samostatná okna a nahradí je `UserControl` panely uvnitř jednoho `AppWindow`.

---

### 2.1 Refaktorovat stávající okna na `UserControl` panely

**Proč:** Aktuálně existují tři samostatná WPF okna – `SettingsWindow`, `HistoryWindow`, `AboutWindow`.
Každé se otevírá zvlášť přes tray menu a vzájemně se navzájem zavírají (`CloseSecondaryWindow`).
Uživatel chce, aby tato okna byla pohromadě jako záložky v jednom okně, nikoli jako tři oddělené.

**Co se změní:**
- Vzniknou nové složky `Views/Panels/`.
- Stávající XAML obsah se přesune do nových `UserControl` souborů:
  - `Views/Panels/SettingsPanel.xaml` + `.cs`
  - `Views/Panels/HistoryPanel.xaml` + `.cs`
  - `Views/Panels/AboutPanel.xaml` + `.cs`
- Původní samostatná okna (`SettingsWindow.xaml/.cs`, `HistoryWindow.xaml/.cs`, `AboutWindow.xaml/.cs`) se smažou.

---

### 2.2 Vytvořit nové `AppWindow` se záložkami

**Proč:** Potřebujeme nové hostitelské okno, které drží `TabControl` se záložkami Settings, Transcriptions, About.

**Co se změní:**
- Vznikne `Views/AppWindow.xaml` + `.cs` s `TabControl`.
- Záložky budou hostovat příslušné panely z 2.1.
- `AppWindow` bude singleton – druhé volání jen přepne záložku a aktivuje okno, neotvírá novou instanci.
- Veřejná metoda `Show(int tabIndex)` umožní tray menu otevřít okno rovnou na správné záložce.
- Tlačítka Save / Cancel pro nastavení budou v patičce `AppWindow` (sdílená pro celé okno), ne uvnitř panelu.

---

### 2.3 Aktualizovat `App.xaml.cs` – nahradit `ShowAbout/History/Settings` voláním `AppWindow`

**Co se změní:**
- `App.ShowAbout()`, `App.ShowHistory()`, `App.ShowSettings()` budou všechny volat `AppWindow.Show(tabIndex)`.
- Pomocná metoda `CloseSecondaryWindow()` a field `_secondaryWindow` se odstraní – `AppWindow` singleton je svůj vlastní správce.
- Tray menu i `DoubleClick` na tray ikoně zachovají stejné chování navenek.

---

### 2.4 Zajistit viditelnost `AppWindow` v Alt+Tab

**Proč:** Uživatel chce, aby se sekundární okna (Settings, History, About) zobrazovala v Alt+Tab
přepínači jako normální aplikační okna. Zároveň `MainWindow` nesmí být nikdy v Alt+Tab vidět.

**Co se změní:**
- `MainWindow` zůstane beze změny – `WS_EX_TOOLWINDOW` je nastaven v `OnSourceInitialized`.
- `AppWindow` dostane explicitně `ShowInTaskbar = true` a zajistí se, že nemá nastaven `WS_EX_TOOLWINDOW` style.
- Ověřit, že owner není nastaven tak, aby `AppWindow` zdědilo styl `MainWindow`.

---

## Fáze 3 – Malé izolované změny

> Nejmenší dopad na kód – každá změna je nezávislá na ostatních.
> Lze implementovat v libovolném pořadí, ideálně mezi většími fázemi.

---

### 3.1 Přidat 300 ms ticha na začátek nahrávky pro Whisper

**Proč:** Whisper občas zahodí první písmeno nebo slabiku nahrávky, pokud neexistuje žádný náběhový čas.
Přidání 300 ms ticha (nulové PCM bajty) před skutečnou nahrávku tento artifact eliminuje.

**Co se změní:**
- V `MainWindow.OnPttStopped`, po zavolání `_recorder.StopRecording()`, se k výsledným WAV bytům prepend
  300 ms ticha: `300 ms × 16 000 Hz × 2 bytes = 9 600 bytů` nulových hodnot vložených za WAV hlavičku před PCM data.
- Alternativně lze upravit `AudioRecorder.StopRecording()` tak, aby ticho přidávala sama jako volitelný parametr.
- Pro Parakeet platí jiné výchozí ticho (250 ms), řeší se v Fázi 4 samostatně v `ParakeetService`.

---

### 3.2 Přidat do tray menu položku „Kopírovat poslední přepis"

**Proč:** Uživatel chce rychle zkopírovat poslední přepis do schránky přímo z tray menu, bez nutnosti
otevírat okno Historie.

**Co se změní:**
- V `App.xaml.cs` se do tray menu přidá položka `"Copy last transcription"` umístěná hned nad `"Transcriptions"`.
- Kliknutí: pokud `App.History.Entries` není prázdný, zavolá `Clipboard.SetText(App.History.Entries[0].Text)`.
- Pokud je historie prázdná, položka bude mít `Enabled = false`.
- Neotvírá se žádné okno.

---

### 3.3 Zvukové upozornění při selhání injektáže textu

**Proč:** Když se `SendInput` nepovede (vrátí méně odeslaných eventů než očekáváme), uživatel se to
nijak nedozví – text se nezobrazí v cílovém okně a aplikace jen tiše selže. Krátký systémový zvuk
okamžitě upozorní, že text se nevložil.

**Co se změní:**
- `TextInjector.InjectText` změní návratový typ z `void` na `bool` (vrací `true` = vše odesláno, `false` = aspoň jeden event nebyl přijat).
- Do `AppSettings` se přidá vlastnost `InjectionFailSound` typu `string` s výchozí hodnotou `"Default"`.
  Povolené hodnoty: `"None"`, `"Default"`, `"Asterisk"`, `"Exclamation"`, `"Hand"`, `"Question"`.
  Tato hodnota říká, který z Windows systémových zvuků přehrát.
- V `MainWindow.OnPttStopped`, v `Task.Run` bloku po `InjectText`, se při `false` návratové hodnotě přehraje zvolený zvuk pomocí `System.Media.SystemSounds`.
- Do `SettingsWindow` (resp. budoucího `SettingsPanel`) se přidá ComboBox pro výběr zvuku.

---

### 3.4 Do nastavení přidat volbu pro překlad

Vyzkoumat jak funguje překládání textu - jaké jsou možnsoti, a podle toho rozšířit nastavení o checkbox nebo další combobox.

---

## Fáze 4 – Podpora GPU backendů

> Střední dopad – přidání NuGet balíčků, rozšíření stávajících servisních tříd a Settings.
> Nezávisí na Fázích 2 a 3, ale musí proběhnout před implementací Parakeet (Fáze 5).

---

### 4.1 Vulkan backend pro Whisper.net

**Proč:** Aplikace aktuálně na GPU funguje pouze přes CUDA. Vulkan je cross-vendor GPU API,
které funguje i na AMD a Intel GPU. Whisper.net má pro Vulkan samostatný runtime balíček.

**Co se změní:**
- Přidat NuGet balíček `Whisper.net.Runtime.Vulkan` do `.csproj` (ověřit kompatibilitu s verzí 1.9.0).
- Rozšířit `WhisperService` o metodu nebo enum `DetectWhisperBackend()` vracející `WhisperBackend { Cuda, Vulkan, Cpu }`.
  Nejprve probe CUDA (stávající logika), pak probe `vulkan-1.dll` přes `LoadLibraryEx`.
- Status label v `MainWindow` zobrazí `"Ready (GPU/Vulkan)"` nebo `"Ready (GPU/CUDA)"` nebo `"Ready (CPU)"`.
- `WhisperFactory.FromPath` vybírá runtime backend automaticky dle přítomnosti NuGet runtime balíčků – žádná ruční konfigurace session není nutná.

---

### 4.2 DirectML backend pro ONNX Runtime (příprava pro Parakeet)

**Proč:** ONNX Runtime, který bude Parakeet Service využívat (Fáze 5), nativně podporuje DirectML –
GPU akceleraci dostupnou na každém Windows 10/11 systému bez závislosti na CUDA nebo specifickém vendoru.

**Co se změní:**
- Přidat NuGet balíček `Microsoft.ML.OnnxRuntime.DirectML` jako podmíněný balíček (pouze `win-x64`).
- Do `AppSettings` přidat vlastnost `OrtBackend` s hodnotami `Auto`, `Cpu`, `DirectMl`.
  (`Cuda` přijde v 4.3.) Výchozí hodnota: `Auto`.
- `Auto` bude znamenat: zkus DirectML, fallback CPU.
- DirectML vyžaduje speciální nastavení session (`parallel_execution = false`, `memory_pattern = false`) –
  tato nastavení budou zdokumentována komentáři přímo v kódu `ParakeetService`.
- Do `SettingsWindow` / `SettingsPanel` přidat volbu backendu pro ORT.

---

### 4.3 CUDA backend pro ONNX Runtime

**Proč:** Pro uživatele s NVIDIA GPU a CUDA toolkitem bude CUDA inference výrazně rychlejší než DirectML.

**Co se změní:**
- Přidat NuGet balíček `Microsoft.ML.OnnxRuntime.Gpu` jako podmíněný balíček (pouze `win-x64`).
- Rozšířit `OrtBackend` enum o hodnotu `Cuda`.
- Probe: zkontrolovat existenci `onnxruntime_providers_cuda.dll` v adresáři `runtimes/cuda/win-x64`.
- Aktualizovat MSBuild target `CopyCudaRuntimeDlls` v `.csproj`, aby zkopíroval i ORT CUDA DLL soubory.

---

### 4.4 const-me/whisper

Zkusit implementovat na Windows rozpoznání podpory pro https://github.com/const-me/whisper,
ale musí to být na jistotu, protože tato knihovna nemá fallback pro CPU, ale je 3.5x rychlejší v inferenci.

**Co se změní:**
- je třeba vytvořit C# wrapper,
- je třeba tuto knihovnu přidat do projektu, detekovat Windows podporu při startu 
  a podle toho zvolit jak se bude procesovat zvuk,
- podle podpory nabízet stažení modelů, které pro tento procesing musí být v jiném formátu:
  https://huggingface.co/ggerganov/whisper.cpp/tree/main
- procesovat zvuk touto knihovnou a modely.

---

## Fáze 5 – Parakeet V3 engine

> Největší nová feature – nový transkripční engine, nová stahovaná data, nová servisní třída, změny settings.
> Závisí na Fázi 1 (ITranscriptionService), Fázi 2 (SettingsPanel detekce modelů) a Fázi 4 (ORT backendy).

---

### 5.1 Rozšíření `download-models.ps1` o stažení Parakeet V3

**Proč:** Parakeet model je distribuován jako `.tar.gz` archiv (nikoli jako jediný `.bin` soubor jako
Whisper GGML modely). Po rozbalení vznikne **adresář** s ONNX soubory. Stávající skript umí pouze stáhnout
a přejmenovat soubor – je třeba přidat rozbalování archivu.

**Co se změní:**
- Do tabulky modelů v `download-models.ps1` se přidá:
  ```
  Parakeet TDT 0.6B V3 (parakeet-tdt-0.6b-v3-int8)
    Velikost: 456 MB | VRAM: ~1.5 GB
    Jazyky: 25 evropských jazyků + ruština a ukrajinština (vč. češtiny)
    URL: https://blob.handy.computer/parakeet-v3-int8.tar.gz
    SHA256: 43d37191602727524a7d8c6da0eef11c4ba24320f5b4730f1a2497befc2efa77
  ```
- Soubor se stáhne jako `.tar.gz.tmp`, po ověření SHA256 se přejmenuje na `.tar.gz`.
- Rozbalení `.tar.gz` v PowerShell:
  - Primární cesta: `tar.exe` (nativně dostupný ve Windows 10 build 17063+).
    Volání: `& tar -xzf "llms\parakeet-v3-int8.tar.gz" -C "llms\"`.
  - Fallback pro starší Windows: inline C# přes `Add-Type` s `System.IO.Compression.GZipStream`
    a `System.IO.Compression.TarFile` (dostupné v .NET 7+, ale PS 5.1 běží na .NET Framework –
    proto se zde použije `tar.exe` jako jediná spolehlivá cesta na Windows 10+).
- Po rozbalení se `.tar.gz` soubor smaže (šetří místo).
- Výsledek: adresář `llms\parakeet-tdt-0.6b-v3-int8\` s ONNX soubory.

---

### 5.2 Detekce modelů v `SettingsPanel` dle typu souboru/adresáře

**Proč:** Stávající `SettingsWindow.BuildModelItems()` kontroluje pouze pevně zadaný seznam 12 `.bin` souborů.
Po přidání Parakeet je třeba detekovat modely dynamicky a rozlišovat Whisper (`.bin`) od Parakeet (adresář).

**Co se změní:**
- Do `AppSettings` se přidá vlastnost `EngineType` s hodnotami `Whisper` a `Parakeet`. Výchozí: `Whisper`.
- `BuildModelItems()` v `SettingsPanel` bude skenovat adresář `llms/`:
  - Soubory s příponou `.bin` → Whisper model.
  - Adresáře, jejichž jméno začíná `parakeet-` a obsahují soubor `encoder-model*.onnx` → Parakeet model.
- Každá položka v ComboBoxu bude vizuálně označena typem: `[Whisper]` nebo `[Parakeet]`.
- Při výběru modelu z ComboBoxu se `EngineType` automaticky nastaví dle detekovaného typu.

---

### 5.3 Implementovat `ParakeetService` (C# + Microsoft.ML.OnnxRuntime)

**Proč:** Jde o samotný nový transkripční engine – přímá C# implementace inference pipeline
nad ONNX Runtime, bez závislosti na Rustu nebo externím procesu.

**Model a jeho formát:**
Soubory v adresáři `parakeet-tdt-0.6b-v3-int8/`:
- `nemo128.onnx` – audio preprocessor (log-mel spektrogram), vstupy: `waveforms [float32]`, `waveforms_lens [int64]`, výstupy: `features`, `features_lens`.
- `encoder-model-int8.onnx` – enkodér, vstupy: `features`, `length`, výstupy: `encoded`, `encoded_len`.
- `decoder_joint-model-int8.onnx` – TDT dekodér + joint network, iterativně volaný v greedy decoding smyčce.
- `vocab.txt` – slovník tokenů, jeden token na řádek, řádek s `<blk>` definuje blank index.

**Pipeline:**
1. Prepend 250 ms ticha (nulové PCM vzorky) před skutečný audio signál.
2. Preprocessor session: `waveforms` → `features` (log-mel), `features_lens`.
3. Encoder session: `features` → `encoded`, `encoded_len`.
4. TDT greedy decoder smyčka: iterativní volání `decoder_joint` session, mapuje výstupy na token IDs
   (implementovat dle Rust source `transcribe-rs/src/onnx/parakeet/mod.rs`).
5. Vocabulary lookup: token IDs → text přes `vocab.txt` slovník.

**Co se změní:**
- Vznikne `Services/ParakeetService.cs` implementující `ITranscriptionService` (z bodu 1.1).
- NuGet: `Microsoft.ML.OnnxRuntime` (přidáno v Fázi 4.2 DirectML nebo 4.3 CUDA).
- `StateChanged` event bude sdílet stejný `TranscriptionState` enum jako `WhisperService`.

---

### 5.4 Přepínání mezi Whisper a Parakeet engine za běhu

**Proč:** `App` musí při startu nebo po změně nastavení vytvořit správnou implementaci `ITranscriptionService`
dle `AppSettings.EngineType`. Při změně v Settings musí starý engine zlikvidovat a nový inicializovat.

**Co se změní:**
- `App` bude mít field `ITranscriptionService _transcriptionService` místo `WhisperService WhisperService`.
- Factory logika v `App.OnStartup` nebo v samostatné metodě `App.CreateTranscriptionService()`:
  dle `EngineType` vytvoří buď `WhisperService` nebo `ParakeetService`.
- Po uložení Settings (pokud se změnil model nebo engine): dispose stará service, vytvoř a inicializuj novou.
- `MainWindow` a ostatní kód pracují výhradně přes `ITranscriptionService` a nepotřebují vědět, který engine běží.

---

## Dokončeno

### ✅ Fáze 1 – Refaktoring a čistota kódu

#### 1.1 Zavést rozhraní `ITranscriptionService`
- Vznikl `Services/ITranscriptionService.cs`.
- `WhisperService` implementuje `ITranscriptionService`.
- `App.WhisperService` má typ `ITranscriptionService`.

#### 1.2 Přejmenovat `EtaStatsService` → `EtaService`, `App.EtaStats` → `App.Eta`
- Třída v `EtaStatsService.cs` přejmenována na `EtaService`.
- `App.EtaStats` → `App.Eta` ve všech souborech.

#### 1.3 Opravit `ContinueWith` + `Unwrap` v `WhisperService.InitializeAsync`
- Blok přepsán na čistý `await`.

#### 1.4 Vyextrahovat logiku pozicování okna do `Services/WindowPositioner.cs`
- Vznikl `Services/WindowPositioner.cs` (~150 řádků).
- `MainWindow.xaml.cs` zkrácen o ~130 řádků.

#### 1.5 Zbavit `TextInjector` přímé závislosti na `App.SettingsService`
- `InjectText` má signaturu `InjectText(string text, IReadOnlyList<int> pttVkCodes)`.
- Volající `MainWindow.OnPttStopped` předává `HotkeyVkCodes` jako argument.

#### 1.6 Nahradit magická čísla pojmenovanými konstantami
- `MainWindow.WavBytesPerSecond = 32000` nahrazuje magic number.
- `WhisperService.MinModelFileSizeBytes` je `public const`.

#### Další opravy coding standards (součást Fáze 1)
- `PowerStatusSnapshot` přesunuto do vlastního souboru `Services/PowerStatusSnapshot.cs` s `public` viditelností.
- `AudioRecorder`, `HotkeyService`: odstraněno `sealed`, přidáno `this.`/`ClassName.`, opraveno pořadí členů.
- `SettingsService`: přidáno `this.` pro instance property, `SettingsService.` pro statické členy.
- `TranscriptionState` přesunuto do `Util/Enums/TranscriptionState.cs`.

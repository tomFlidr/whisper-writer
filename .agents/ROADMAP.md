# WhisperWriter – Roadmap

Tento soubor obsahuje plán všech plánovaných změn aplikace v doporučeném pořadí
implementace. Každá fáze má před implementací vlastní
specifikaci v `.agents/specs/`. Každá hotová položka se přesouvá do sekce **Dokončeno** a
zároveň se aktualizuje `AGENTS.md`.

> **Pravidlo pro zápis:** Do tohoto souboru se zapisují pouze věci, které byly skutečně prodiskutovány a odsouhlaseny.
> Nic se nevymýšlí. Každá položka popisuje konkrétní problém, proč se řeší, a co přesně se změní.
> Každá implementace musí mít automatizované testy podle `.agents/skills/testing.md`.

> Cesty k produkčním zdrojům v tomto plánu jsou relativní k `WhisperWriter/`; cesty k testům jsou
> relativní k sousednímu `WhisperWriter.Tests/`.

---

## Fáze 0 – Základ automatizovaných testů

**Stav:** Lokální testy, inventář a aktuální oddíly `AGENTS.md` dokončeny 4. listopadu 2026.
Hosted GitHub Actions zatím neběželo; výsledek zůstává neověřený.

---

## Fáze 3 – Další GPU backendy a ONNX Runtime

> Další backendy pro Whisper.net a ONNX Runtime; tato fáze musí proběhnout před
> implementací Parakeet (Fáze 7). Nezávisí na Fázích 5 a 6.
> U větších runtime balíčků nejprve prozkoumat distribuci; preferovat stažení s potvrzením
> uživatele při instalaci nebo samostatný instalační skript.
> Technické podklady a ověřovací podmínky: `.agents/specs/phase-3-gpu-and-onnx-backends.md`;
> společná architektura: `.agents/specs/gpu-backend-capability-analysis.md`.
>
> **Výběr:** `Auto` použije ověřené pořadí backendů zvoleného engine. Výslovně zvolená knihovna/provider
> je autoritativní; aplikace ji nesmí potichu nahradit jiným enginem. `Auto` může vybírat jen kompatibilní
> kombinace modelu, knihovny a provideru.

---

### 3.1 Vulkan backend pro Whisper.net

**Proč:** Aplikace má k dispozici Whisper.net CUDA pro kompatibilní NVIDIA hardware. Vulkan je
kandidát na rozšíření kompatibility na další GPU výrobce; konkrétní podporu a rychlost musí ověřit test na cílovém
hardware. Fáze 1 neprokázala rychlostní přínos Const-me a slabá integrovaná Intel UHD karta
z jejího D3D11 smoke testu není reprezentativní pro Vulkan.

**Předběžné ověření:** Scratch probe z 9. 11. 2026 s Whisper.net 1.9.0 na Windows 11 x64
načetla režim Auto jako CUDA a po Vulkan-prioritním výběru načetla Vulkan a přepsala `01.wav`.
Byl vyžádán i Vulkan `GpuDevice=1`; `vulkaninfo` na hostu uvádí Intel UHD jako GPU 1. Whisper.net
však nevrátil název fyzického zařízení, takže toto mapování není přímo potvrzené. Žádná AMD karta
nebyla testována. Podrobnosti a omezení jsou v `.agents/specs/phase-3-gpu-and-onnx-backends.md`.

**Co se změní:**
- Před produkční integrací ověřit čistý publish, podporovaný runtime/ovladač a požadavky Vulkan
  balíčku na Windows 11 x64; zachovat CPU-only cestu pro `win-x86`.
- Přidat `Whisper.net.Runtime.Vulkan` ve stejné verzi jako ostatní Whisper.net balíčky, případně
  nejprve schválit a společně aktualizovat všechny runtime reference.
- Použít automatický výběr Whisper.net; po inicializaci načíst skutečný backend přes
  `RuntimeOptions.LoadedLibrary`. Samotná přítomnost `vulkan-1.dll` ani CUDA DLL není důkazem,
  že daný backend skutečně inicializoval model.
- Upravit aktuální `Whisper`/`IWhisperRuntime` a status v `MainWindow`: současná aplikace odvozuje
  `GPU`/`CPU` pouze z CUDA DLL probe, takže by mohla Vulkan chybně označit jako CPU.
- Ověřit fallback na CPU, načtení a přepis modelu i čistý single-file publish na skutečných
  Windows x64 GPU; zvlášť otestovat reprezentativní AMD/Intel hardware před tvrzením o jeho podpoře.

---

### 3.2 DirectML backend pro ONNX Runtime (příprava pro Parakeet)

**Proč:** DirectML je samostatný execution provider ONNX Runtime pro plánovaný Parakeet engine
(Fáze 7), nikoli Whisper.net backend. Jeho použitelnost závisí na podporovaném Windows/GPU/ovladači
a pokrytí operátorů modelem; nelze předpokládat akceleraci na každém Windows 11 počítači.
Před výběrem DirectML porovnat také aktuální doporučení Microsoftu pro WinML.

**Co se změní:**
- Přidat `Microsoft.ML.OnnxRuntime.DirectML` jen po ověření dostupnosti a kompatibility jeho zvolené
  verze s hlavním `Microsoft.ML.OnnxRuntime` balíčkem a podporovanými modely/zařízeními (`win-x64`).
- Do `AppSettings` přidat vlastnost `OrtBackend` s hodnotami `Auto`, `Cpu`, `DirectMl`.
  (`CUDA` přijde v 3.3.) Výchozí hodnota: `Auto`.
- `Auto` zkusí nakonfigurovat DirectML a při nedostupnosti použije CPU; log/status musí potvrdit
  skutečný execution provider, nikoli jen přítomnost DLL.
- Pokud uživatel výslovně zvolí `DirectMl` a provider nejde inicializovat, zobrazit chybu/nedostupnost;
  nesmí se potichu přejít na jiný provider nebo engine. Automatický fallback patří pouze do `Auto`.
- DirectML vyžaduje speciální nastavení session (`parallel_execution = false`, `memory_pattern = false`) –
  tato nastavení budou zdokumentována komentáři přímo v kódu `ParakeetService`.
- Ověřit, jak pokrytí ONNX operátorů DirectML ovlivňuje skutečný model a paralelní požadavky;
  plné využití GPU ani souběh na jedné session se nesmí předpokládat.
- Do `SettingsWindow` / `SettingsPanel` přidat volbu backendu pro ORT.

---

### 3.3 CUDA backend pro ONNX Runtime

**Proč:** CUDA je další ONNX Runtime provider pro kompatibilní NVIDIA zařízení. Jeho rychlostní
výhoda proti DirectML je hypotéza, kterou je nutné změřit na Parakeet modelu a cílovém hardware.

**Co se změní:**
- Přidat `Microsoft.ML.OnnxRuntime.Gpu` jako CUDA provider pouze pro `win-x64` po ověření, že je
  správným balíčkem pro zvolenou verzi ONNX Runtime; všechny ORT reference verzovat kompatibilně.
- Rozšířit `OrtBackend` enum o hodnotu `Cuda`.
- Při inicializaci ověřit skutečné vytvoření CUDA execution provideru; samotná existence
  `onnxruntime_providers_cuda.dll` není potvrzením, že session používá CUDA.
- Při výslovné volbě `CUDA` nepřejít potichu na jiný provider/engine; fallback je řízený pouze
  nastavením `Auto` a skutečně načtený provider musí být viditelný.
- Zjistit přesné CUDA/cuDNN a nativní DLL požadavky vybraného ORT balíčku. Nepředpokládat,
  že současný `CopyCudaRuntimeDlls` target pro Whisper pokryje závislosti ONNX Runtime.
- Porovnat CUDA, DirectML/WinML a CPU na stejném modelu; zaznamenat rychlost, kvalitu,
  paměť, balení a fallback místo předpokladu, že CUDA bude vždy rychlejší.

---

## Fáze 4 – dodělat hrubý refactoring

Původní refaktorovací položky jsou v sekci **Dokončeno**. Přechod na Avalonia je odložen do Fáze 8.

## Fáze 5 – Sjednocení sekundárních oken do jednoho tabbed okna

> Výrazný dopad na kód – kompletně přestrukturuje složku `Views/`,
> odstraní tři samostatná okna a nahradí je `UserControl` panely uvnitř jednoho `AppWindow`.

---

### 5.1 Refaktorovat stávající okna na `UserControl` panely

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

### 5.2 Vytvořit nové `AppWindow` se záložkami

**Proč:** Potřebujeme nové hostitelské okno, které drží `TabControl` se záložkami Settings, Transcriptions, About.

**Co se změní:**
- Vznikne `Views/AppWindow.xaml` + `.cs` s `TabControl`.
- Záložky budou hostovat příslušné panely z 5.1.
- `AppWindow` bude singleton – druhé volání jen přepne záložku a aktivuje okno, neotvírá novou instanci.
- Veřejná metoda `Show(int tabIndex)` umožní tray menu otevřít okno rovnou na správné záložce.
- Tlačítka Save / Cancel pro nastavení budou v patičce `AppWindow` (sdílená pro celé okno), ne uvnitř panelu.
- Při odchodu na jinou záložku nebo zavření `AppWindow` s neuloženými změnami nabídnout
  jejich uložení a pokračování, nebo zahození změn a pokračování.
- Podrobné chování potvrzovacího dialogu bude popsáno ve specifikaci Fáze 5 před implementací.

---

### 5.3 Aktualizovat `App.xaml.cs` – nahradit `ShowAbout/History/Settings` voláním `AppWindow`

**Co se změní:**
- `App.ShowAbout()`, `App.ShowHistory()`, `App.ShowSettings()` budou všechny volat `AppWindow.Show(tabIndex)`.
- Pomocná metoda `CloseSecondaryWindow()` a field `_secondaryWindow` se odstraní – `AppWindow` singleton je svůj vlastní správce.
- Tray menu i `DoubleClick` na tray ikoně zachovají stejné chování navenek.

---

### 5.4 Zajistit viditelnost `AppWindow` v Alt+Tab

**Proč:** Uživatel chce, aby se sekundární okna (Settings, History, About) zobrazovala v Alt+Tab
přepínači jako normální aplikační okna. Zároveň `MainWindow` nesmí být nikdy v Alt+Tab vidět.

**Co se změní:**
- `MainWindow` zůstane beze změny – `WS_EX_TOOLWINDOW` je nastaven v `OnSourceInitialized`.
- `AppWindow` dostane explicitně `ShowInTaskbar = true` a zajistí se, že nemá nastaven `WS_EX_TOOLWINDOW` style.
- Ověřit, že owner není nastaven tak, aby `AppWindow` zdědilo styl `MainWindow`.

---

## Fáze 6 – Malé izolované změny

> Nejmenší dopad na kód – každá změna je nezávislá na ostatních.
> Lze implementovat v libovolném pořadí, ideálně mezi většími fázemi.

---

### 6.1 Přidat 300 ms ticha na začátek nahrávky pro Whisper

**Proč:** Whisper občas zahodí první písmeno nebo slabiku nahrávky, pokud neexistuje žádný náběhový čas.
Přidání 300 ms ticha (nulové PCM bajty) před skutečnou nahrávku tento artifact eliminuje.

**Co se změní:**
- V `MainWindow.OnPttStopped`, po zavolání `_recorder.StopRecording()`, se k výsledným WAV bytům prepend
  300 ms ticha: `300 ms × 16 000 Hz × 2 bytes = 9 600 bytů` nulových hodnot vložených za WAV hlavičku před PCM data.
- Alternativně lze upravit `AudioRecorder.StopRecording()` tak, aby ticho přidávala sama jako volitelný parametr.
- Pro Parakeet platí jiné výchozí ticho (250 ms), řeší se v Fázi 7 samostatně v `ParakeetService`.

---

### 6.2 Přidat do tray menu položku „Kopírovat poslední přepis"

**Proč:** Uživatel chce rychle zkopírovat poslední přepis do schránky přímo z tray menu, bez nutnosti
otevírat okno Historie.

**Co se změní:**
- V `App.xaml.cs` se do tray menu přidá položka `"Copy last transcription"` umístěná hned nad `"Transcriptions"`.
- Kliknutí: pokud `App.History.Entries` není prázdný, zavolá `Clipboard.SetText(App.History.Entries[0].Text)`.
- Pokud je historie prázdná, položka bude mít `Enabled = false`.
- Neotvírá se žádné okno.

---

### 6.3 Zvukové upozornění při selhání injektáže textu

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

### 6.4 Do nastavení přidat volbu pro překlad

Cílem je, aby šel překlad skutečně používat přímo v aplikaci. Výchozí stav překladu musí být vypnutý.
Konkrétní režim, cílový jazyk a ovládání se určí po průzkumu a v samostatné specifikaci.

---

## Fáze 7 – Parakeet V3 engine

> Největší nová feature – nový transkripční engine, nová stahovaná data, nová servisní třída, změny settings.
> Závisí na Fázi 3 (ORT backendy), Fázi 4 (ITranscriptionService) a Fázi 5 (SettingsPanel detekce modelů).
> U větších modelů nejprve prozkoumat distribuci; preferovat samostatné stažení s potvrzením
> uživatele nebo instalační skript.

---

### 7.1 Rozšíření `WhisperWriter/download-models.ps1` o stažení Parakeet V3

**Proč:** Parakeet model je distribuován jako `.tar.gz` archiv (nikoli jako jediný `.bin` soubor jako
Whisper GGML modely). Po rozbalení vznikne **adresář** s ONNX soubory. Stávající skript umí pouze stáhnout
a přejmenovat soubor – je třeba přidat rozbalování archivu.

**Co se změní:**
- Do tabulky modelů v `WhisperWriter/download-models.ps1` se přidá:
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

### 7.2 Detekce modelů v `SettingsPanel` dle typu souboru/adresáře

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

### 7.3 Implementovat `ParakeetService` (C# + Microsoft.ML.OnnxRuntime)

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
- Vznikne `Services/ParakeetService.cs` implementující `ITranscriptionService` (z bodu 4.1).
- NuGet: `Microsoft.ML.OnnxRuntime` (přidáno v Fázi 3.2 DirectML nebo 3.3 CUDA).
- `StateChanged` event bude sdílet stejný `TranscriptionState` enum jako `WhisperService`.

---

### 7.4 Přepínání mezi Whisper a Parakeet engine za běhu

**Proč:** `App` musí při startu nebo po změně nastavení vytvořit správnou implementaci `ITranscriptionService`
dle `AppSettings.EngineType`. Při změně v Settings musí starý engine zlikvidovat a nový inicializovat.

**Co se změní:**
- `App` bude mít field `ITranscriptionService _transcriptionService` místo `WhisperService WhisperService`.
- Factory logika v `App.OnStartup` nebo v samostatné metodě `App.CreateTranscriptionService()`:
  dle `EngineType` vytvoří buď `WhisperService` nebo `ParakeetService`.
- Po uložení Settings (pokud se změnil model nebo engine): dispose stará service, vytvoř a inicializuj novou.
- `MainWindow` a ostatní kód pracují výhradně přes `ITranscriptionService` a nepotřebují vědět, který engine běží.

---

## Fáze 8 – Průzkum a případný přechod na Avalonia

**Proč:** Do dokončení Fází 0–7 se projekt soustředí na Windows. Teprve potom se vyhodnotí,
které části aplikace lze přenést.

**Co se změní:**
- Nejprve se provede průzkum přenositelnosti jednotlivých částí a jejich závislostí.
- Rozsah migrace a podporované platformy se po průzkumu určí v samostatné specifikaci.
- Následně se přenesou pouze odsouhlasené části, které se ukážou jako přenositelné.

---

## Dokončeno

### ✅ Fáze 0 – Lokálně ověřená testová základna a dokumentace

- Přidán testovací projekt xUnit v3 + VSTest pro `net8.0-windows7.0`, zařazený v solution.
- Přidána `.github/workflows/tests.yml` pro pull requesty a push na `main`.
- V `.agents/specs/phase-0-coverage-inventory.md` je mapováno 95 behavior-bearing
  public/protected kontraktů a všech 15 routovaných event bindingů ve čtyřech view XAML souborech.
- Finální lokální ověření 4. 11. 2026: **185/185 testů Debug**, **185/185 Release**;
  aplikační build v obou konfiguracích: **0 warningů, 0 chyb**.
- Non-zero návratový kód `dotnet test` při selhání assertion byl ověřen dočasným testem,
  který byl následně odstraněn.
- Hosted GitHub Actions zatím neběželo, takže výsledek CI na hostovaném runneru zůstává neověřený.
- Oddíly 1–9 v `AGENTS.md` jsou sladěné s aktuálním stromem, testy, build příkazy a nastaveními;
  Appendix A je označený jako historický kontext.
- Fáze 0 sama skončila bez produktového chování; požadavek RED → GREEN → REFACTOR platí pro
  pozdější implementační změny.

### ✅ Fáze 1 – Vyhodnocení GPU backendu (uzavřeno bez integrace)

**Zadání:** Vyhodnotit backend Const-me/Whisper jako alternativu k Whisper.net CUDA.
Ověřit sestavitelnost a licenci a porovnat rychlost i přesnost na českých nahrávkách.

**Výsledky:**
- Sestavitelnost a licenční podmínky `Const-me/Whisper` byly ověřeny v izolovaném scratch prostoru.
- Na 10 českých nahrávkách se stejným modelem `ggml-medium.bin`, jazykem `cs` a promptem
  byla stávající Whisper.net CUDA cesta rychlejší než Const-me D3D11.
- Ověřil se běh stávajícího `ggml-medium.bin`; ostatní GGML modely aplikace nebyly
  v této evaluaci jednotlivě testovány.
- Součet času přepisu: Whisper.net CUDA **144,040 s**; Const-me přes Media Foundation
  **148,296 s**; Const-me přes přímý PCM buffer **147,110 s**. Ani jedna varianta Const-me
  nepřinesla celkovou úsporu času.
- Vážená WER byla **11,23 %** u Whisper.net i varianty Media Foundation; přímý PCM měl
  **11,38 %**. CER byla **2,92 %** u Whisper.net, **2,72 %** u Media Foundation
  a **2,66 %** u přímého PCM. Zlepšení znakové chybovosti je malé a u přímého PCM
  přibyla jedna slovní chyba.
- Const-me se podařilo spustit také na lokálním Intel UHD přes Direct3D 11. Tento adaptér
  je integrovaný a slabý; jeho pomalý výsledek **není zobecněním výkonu D3D11** ani AMD
  GPU. Na 24,9s nahrávce trval přepis 218,6s, zatímco Whisper.net CPU 19,3s. Smoke test
  potvrzuje pouze funkčnost této konkrétní karty; žádný AMD GPU ani starší počítač nebyl
  testován.
- Závěr: Const-me se neintegruje jako rychlejší backend. Případná širší podpora AMD/Intel
  je samostatný cíl kompatibility pro pozdější fázi a vyžaduje ověření na reprezentativním
  hardwaru. Fáze 3.1 už obsahuje Vulkan backend pro Whisper.net.
- Projekt už obsahuje rozhraní `ITranscription`; roadmapová Fáze 4.1 používá odlišný název
  `ITranscriptionService`, proto je nutné před další integrací pracovat s aktuálním kontraktem.
- Čistý .NET single-file publish nezahrnul nativní `Whisper.dll`; integrace by vyžadovala
  explicitní kopírování nebo ověřené rozbalení nativní knihovny. Balíček je určen pro x64;
  současný CPU fallback musí zůstat pro x86 a zařízení bez vhodného GPU.
- Testovaný balíček `WhisperNet 1.12.0` používá `ComLightInterop 1.3.7` a licenci MPL-2.0;
  před distribucí je třeba zachovat požadované licenční a zdrojové informace.
- Produkční kód ani NuGet reference se během evaluace nezměnily; fáze se uzavírá jako
  dokončený průzkum, nikoli jako implementovaná GPU akcelerace.
- Zdrojový projekt pro tuto evaluaci byl stažen mimo repozitář do
  `D:\llms\hermes\cache\scratch\const-me-whisper-spike-2026-11-05` (dočasný scratch,
  může být automaticky odstraněn po 24 hodinách nečinnosti).
  Použitý commit: `1ce7399704d8bea92429fc429819d32991fe9152`. Trvalý upstream zdroj:
  [Const-me/Whisper](https://github.com/Const-me/Whisper); nativní projekt je ve `Whisper/`,
  C# wrapper v `WhisperNet/`.
- Úplné výsledky a předávací poznámky: `.agents/specs/gpu-backend-benchmark-handoff.md`.

### ✅ Fáze 2 – Paralelní nahrávání a transkripce přes hotkey

**Dokončeno:** 10. listopadu 2026.

- Přidána FIFO fronta dokončených audio dávek a adaptivní inference kapacita. Každý job zachovává
  vlastní audio, nastavení a focus snapshot; pro stejné cílové okno se zachová pořadí nahrávání.
- Nastavitelné výchozí limity jsou 10 outstanding jobů a 128 MiB retained audio. Audio limit je
  měkký: přijatá nahrávka se nezkrátí, nezahodí ani nemusí opakovat. Při překročení se pozastaví
  pouze další PTT pokusy až do dokončení všech outstanding jobů; po uvolnění kapacity se indikátor
  automaticky vrátí z šedé do modré.
- Widget zobrazuje až tři ETA v pořadí výsledků a oddělené počty aktivních a čekajících přepisů.
  Při startu ukazuje `GPU Detection…`; CUDA/model inicializace běží mimo WPF Dispatcher.
- Ověřeno lokálně: Debug i Release **217/217 testů**, oba aplikační buildy **0 warningů, 0 chyb**.
  Read-only DXGI smoke test proběhl; živý mikrofon/PTT v produkční aplikaci nebyl otestován.
- Podrobný návrh, rozhodnutí, akceptační testy a omezení: `.agents/specs/phase-2-parallel-transcription.md`.

> Starší dokončené položky níže uchovávají historii a mohou používat tehdejší názvy souborů/tříd.
> Pro aktuální strom použij zdrojový kód a `.agents/specs/phase-0-coverage-inventory.md`.

### ✅ Fáze 4 – Refaktoring a čistota kódu

#### 4.1 Zavést rozhraní `ITranscriptionService`
- Vznikl `Services/ITranscriptionService.cs`.
- `WhisperService` implementuje `ITranscriptionService`.
- `App.WhisperService` má typ `ITranscriptionService`.

#### 4.2 Přejmenovat `EtaStatsService` → `EtaService`, `App.EtaStats` → `App.Eta`
- Třída v `EtaStatsService.cs` přejmenována na `EtaService`.
- `App.EtaStats` → `App.Eta` ve všech souborech.

#### 4.3 Opravit `ContinueWith` + `Unwrap` v `WhisperService.InitializeAsync`
- Blok přepsán na čistý `await`.

#### 4.4 Vyextrahovat logiku pozicování okna do `Services/WindowPositioner.cs`
- Vznikl `Services/WindowPositioner.cs` (~150 řádků).
- `MainWindow.xaml.cs` zkrácen o ~130 řádků.

#### 4.5 Zbavit `TextInjector` přímé závislosti na `App.SettingsService`
- `InjectText` má signaturu `InjectText(string text, IReadOnlyList<int> pttVkCodes)`.
- Volající `MainWindow.OnPttStopped` předává `HotkeyVkCodes` jako argument.

#### 4.6 Nahradit magická čísla pojmenovanými konstantami
- `MainWindow.WavBytesPerSecond = 32000` nahrazuje magic number.
- `WhisperService.MinModelFileSizeBytes` je `public const`.

#### Další opravy coding standards (součást Fáze 4)
- `PowerStatusSnapshot` přesunuto do vlastního souboru `Services/PowerStatusSnapshot.cs` s `public` viditelností.
- `AudioRecorder`, `HotkeyService`: odstraněno `sealed`, přidáno `this.`/`ClassName.`, opraveno pořadí členů.
- `SettingsService`: přidáno `this.` pro instance property, `SettingsService.` pro statické členy.
- `TranscriptionState` přesunuto do `Util/Enums/TranscriptionState.cs`.

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

> Fáze 3 obsahuje provider základ, provisioning CUDA/cuDNN, Parakeet V3 engine (3.5) a transakční
> přepínání engine (3.6). Nezávisí na pozdějších Fázích 4–7.
> U větších runtime balíčků nejprve prozkoumat distribuci; preferovat stažení s potvrzením
> uživatele při instalaci nebo samostatný instalační skript.
> Technické podklady a ověřovací podmínky: `.agents/specs/phase-3-gpu-and-onnx-backends.md`;
> společná architektura: `.agents/specs/gpu-backend-capability-analysis.md`.
> Průběžný stav a bod obnovení: `.agents/phase-3-progress.md`; načíst při zahájení/pokračování práce
> na Fázi 3 a před shrnutím nebo předáním kontextu.
>
> **Výběr:** `Auto` použije ověřené pořadí backendů zvoleného engine. Výslovně zvolená knihovna/provider
> je autoritativní; aplikace ji nesmí potichu nahradit jiným enginem. `Auto` může vybírat jen kompatibilní
> kombinace modelu, knihovny a provideru.
>
> **Fáze 3.0 je dokončena** a přesunuta do sekce **Dokončeno**. Provider pakety 3.2/3.3 byly schváleny
> a implementovány jako oddělené x64 projekty: CPU ORT 1.31.1, DirectML 1.24.4 a CUDA 1.31.1. WPF host
> sdílí pouze vlastní kontrakt a v jednom procesu používá jen jeden nativní ORT core; přepnutí vyžaduje restart.
> Produkční loader přes syntetické vstupy spustil všechny tři Parakeet v3 int8 grafy na CPU, DirectML i CUDA;
> profily zaznamenaly CPU plus vybraný GPU provider. Nejde o audio-to-text, benchmark ani kvalitu přepisu.
> X64 app ZIP obsahuje CPU pack; DirectML je volitelný ZIP v releasu. ONNX CUDA provider pack workflow
> sestaví jen jako krátkodobý CI artefakt, ale v1.4.0 jej nepřipojí k veřejnému releasu. Důvodem je jeho
> velikost a nedokončené doručení CUDA/cuDNN runtime. README dočasně odděluje současné Whisper.net CUDA
> od budoucího ONNX CUDA pro Parakeet; instalace CUDA sama Parakeet inference v1.4.0 nezapne.
> Hosted GitHub Actions zatím neběželo. Podrobnosti jsou ve specifikaci Fáze 3.
> **Fáze 3 není dokončena:** 3.4 musí vyřešit doručení CUDA/cuDNN bez ručního hledání DLL; 3.5
> implementuje Parakeet V3 a 3.6 přidá transakční interaktivní přepnutí engine. Všechny tři položky jsou
> povinné pro uzavření fáze.

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

**Co se změnilo / zbývá:**
- `Whisper.net.Runtime.Vulkan` 1.9.0 je přidaný v souladu s `Whisper.net`, CPU a CUDA runtime 1.9.0.
- Release restore předává `RuntimeIdentifier`, aby se x64-only CUDA/Vulkan balíčky správně vyloučily
  z `win-x86`; CUDA kopírovací target se také řídí RID.
- Lokální čistý publish je ověřen: `win-x64` obsahuje pět Vulkan DLL a tři CUDA runtime DLL; `win-x86`
  obsahuje x86 CPU runtime a žádný CUDA/Vulkan adresář. Hosted Release workflow zatím neběželo.
- Dne 11. 11. 2026 proběhlo lokální srovnání na všech 10 českých WAV souborech (349,780 s audia):
  CUDA request total **198,134 s**, Vulkan **125,208 s**; Vulkan byl rychlejší ve všech 10 párech.
  Vážená WER byla shodná **73/650 (11,23 %)**; CER bylo CUDA **121/4093 (2,956 %)** a Vulkan
  **122/4093 (2,981 %)**. Celkový rozdíl request času činil **72,926 s (36,81 %)** ve prospěch Vulkan.
  Transcript text nebyl uložen; metriky jsou pouze ve scratch JSON uvedeném ve specifikaci.
- Čerstvý `win-x86` publish byl 11. 11. spuštěn s modelem `ggml-small.bin`; widget dosáhl `Ready (Cpu)`.
  x86 je tím ověřen jako launch/CPU initialization, ne jako audio-to-text transkripce.
- Automatický výběr Whisper.net po inicializaci hlásí skutečný runtime přes `RuntimeOptions.LoadedLibrary`.
  Samotná přítomnost `vulkan-1.dll` ani CUDA DLL není důkazem úspěšného backendu.
- Na aktuálním hostu jsou Intel UHD a NVIDIA Quadro T2000; žádný AMD GPU. Před tvrzením o širší AMD/Intel
  kompatibilitě je stále nutný reprezentativní AMD test. Název fyzického Vulkan zařízení runtime stabilně
  neposkytl; srovnání používá požadovaný Vulkan device index 0.
- Používat `ActiveBackendInfo` vytvořené po úspěšné inicializaci; během načítání zůstat u neutrálního
  stavu. Samotný nález DLL není důkazem aktivního runtime.

---

### 3.2 DirectML backend pro ONNX Runtime (příprava pro Parakeet)

**Proč:** DirectML je samostatný execution provider ONNX Runtime pro plánovaný Parakeet engine
(Fáze 3.5), nikoli Whisper.net backend. Jeho použitelnost závisí na podporovaném Windows/GPU/ovladači
a pokrytí operátorů modelem; nelze předpokládat akceleraci na každém Windows 11 počítači.
Před výběrem DirectML porovnat také aktuální doporučení Microsoftu pro WinML.

**Stav implementace:**
- `Microsoft.ML.OnnxRuntime.DirectML` 1.24.4 je pouze v samostatném x64 projektu
  `WhisperWriter.OrtRuntime.DirectML`; není přidán do hlavního WPF outputu.
- `AppSettings.OrtBackend` podporuje `Auto`, `Cpu`, `DirectMl` a `Cuda`. Výchozí `Auto` vybere
  DirectML, pokud je jeho provider pack nainstalovaný; jinak CPU. Selhání DirectML při `Auto` může
  přejít na CPU EP uvnitř stejného ORT core. Výslovné `DirectMl` nesmí tiše přejít na jiný provider.
- DirectML session používá `ExecutionMode.ORT_SEQUENTIAL`, `EnableMemoryPattern=false` a serializuje
  `Run` volání nad stejnou session.
- Aktivní provider se odvozuje až po inference z ORT profilu. Profil všech tří ověřených grafů
  ukázal `DmlExecutionProvider` spolu s `CPUExecutionProvider`; DLL inventář se za aktivní stav nepovažuje.
- UI ukládá volbu ORT provideru, ale ovlivní jen Parakeet engine; Whisper zůstává na Whisper.net.
- Čistý provider pack se publikuje jako volitelný x64 ZIP v samostatné cestě
  `runtimes/onnx/directml`. Obsahuje také license/third-party notices. Release workflow lokálně
  navržen, hosted GitHub Actions zatím neběželo.
- Produkční ALC/loader smoke provedl všechny tři grafy se syntetickými vstupy. Nejde o audio-to-text
  validaci; přesnost, latency, paměť a session-pool chování zůstávají neověřené.

---

### 3.3 CUDA backend pro ONNX Runtime

**Proč:** CUDA je další ONNX Runtime provider pro kompatibilní NVIDIA zařízení. Jeho rychlostní
výhoda proti DirectML je hypotéza, kterou je nutné změřit na Parakeet modelu a cílovém hardware.

**Stav implementace:**
- `Microsoft.ML.OnnxRuntime.Gpu.Windows` 1.31.1 je pouze v samostatném x64 projektu
  `WhisperWriter.OrtRuntime.Cuda`; hlavní WPF output neobsahuje CUDA ORT core.
- CUDA je explicitní volba; při chybě se nepřechází na jiný ORT core/provider. Aktivní CUDA se hlásí
  pouze z ORT profilu po inference; CPU uzly se uvádějí také.
- CUDA ORT vyžaduje kompatibilní CUDA 13 a cuDNN 9. CI-only provider pack pro v1.4.0 tyto runtime DLL
  nezahrnuje; pokud je pack ručně sestaven, host přidá do process PATH nastavenou složku, existující
  `runtimes/cuda/win-x64` (pokud je přítomná) a provider pack. Whisper.net CUDA runtime je oddělený a
  nemusí obsahovat všechny závislosti ORT, zejména cuDNN.
- CUDA provider pack se v1.4.0 nepřipojí k veřejnému releasu; x86 zůstává bez ORT packu.
- Produkční ALC/loader smoke provedl všechny tři ověřené grafy se syntetickými vstupy. Encoder profil
  zaznamenal 742 memcpy uzlů, decoder/joint 21. Výkon, kvalita a úplná transkripční pipeline zůstávají
  neověřené; rychlostní výhodu CUDA nelze předpokládat.

---

### 3.4 Zajistit CUDA/cuDNN runtime pro ONNX CUDA bez ruční instalace

**Proč:** CI-only ONNX CUDA provider pack obsahuje ORT provider, ale ne potřebné CUDA 13/cuDNN 9 runtime
DLL. Vyžadovat po uživateli, aby je samostatně našel, nainstaloval nebo zadával jejich cestu, není hotové
řešení. Tato položka blokuje dokončení Fáze 3.

**Co se musí vyřešit:**
- Zvolit právně a technicky podporovatelnou distribuci: přibalit redistribuovatelné DLL po kontrole
  licencí, nebo implementovat aplikací řízené stažení z autoritativního zdroje s výslovným souhlasem.
- Verze připnout a ověřovat integritu. Běžné použití nesmí záviset na CUDA Toolkit vývojáře, Pythonu,
  PyTorch/ComfyUI, globálním PATH ani ručním kopírování jednotlivých DLL.
- `OrtCudaRuntimePath` může zůstat jako pokročilý override, ne jako povinný krok běžné instalace.
- Ověřit čistý Windows x64 instalační/publish scénář bez předinstalovaných vývojářských runtime DLL a
  skutečný ORT session/inference běh na kompatibilní NVIDIA kartě.
- Zachovat CPU/DirectML funkčnost a x86 bez ONNX providerů; dodat všechny license/third-party notices.

**Stav:** Povinná, zatím neimplementovaná. Pokud redistribuci blokují licence, musí být před volbou
alternativy předložen konkrétní právní/technický důvod a podporované možnosti; ruční cesta k DLL fázi neuzavírá.

---

### 3.5 Parakeet V3 engine

> Největší nová feature – nový transkripční engine, nová stahovaná data, nová servisní třída, změny settings.
> Závisí na provider packách 3.2/3.3 a runtime provisioningu 3.4; používá aktuální kontrakt `ITranscription`.
> U větších modelů nejprve prozkoumat distribuci; preferovat samostatné stažení s potvrzením
> uživatele nebo instalační skript.

---

#### 3.5.1 Rozšíření `WhisperWriter/download-models.ps1` o stažení Parakeet V3

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
- Předběžné ověření ve Fázi 3 (11. 11. 2026): archiv má 478 517 071 bajtů a jeho SHA256 odpovídá
  hodnotě výše. Obsahuje také macOS `._` metadata soubory; extrakce je má bezpečně vynechat.

---

#### 3.5.2 Detekce modelů v `SettingsWindow` podle typu modelu

**Proč:** Whisper používá jednotlivé `.bin` soubory, zatímco Parakeet V3 je adresář s více ONNX soubory.
Výběr modelu musí zůstat kompatibilní s vybraným enginem.

**Co se změní:**
- Do `AppSettings` se přidá `EngineType` se stabilními hodnotami `Whisper` a `Parakeet`; výchozí je `Whisper`.
- `SettingsWindow` bude skenovat `llms/`: `.bin` soubory označí jako Whisper modely a adresáře Parakeet
  uzná jen při přítomnosti `nemo128.onnx`, `encoder-model.int8.onnx`, `decoder_joint-model.int8.onnx`,
  `vocab.txt` a `config.json`.
- Modely budou v UI označené podle engine a seznam se omezí na modely kompatibilní s výběrem.
- Výběr modelu nesmí tiše změnit engine. Změna engine a preflight kandidátního modelu patří do Fáze 3.6.

---

#### 3.5.3 Implementovat `ParakeetService` přes ONNX Runtime provider factory

**Proč:** Jde o samotný Parakeet V3 transkripční engine. C# service orchestruje ONNX sessions přes existující
provider factory; nevolá Rust ani samostatný externí proces.

**Model a jeho formát:**
Soubory v adresáři `parakeet-tdt-0.6b-v3-int8/`:
- `nemo128.onnx` (139 764 B) – audio preprocessor (log-mel spektrogram), vstupy `waveforms [float32]`,
  `waveforms_lens [int64]`; výstupy `features`, `features_lens`.
- `encoder-model.int8.onnx` (652 183 999 B) – enkodér, vstupy `audio_signal [float32]`, `length [int64]`;
  výstupy `outputs`, `encoded_lengths`.
- `decoder_joint-model.int8.onnx` (18 202 004 B) – TDT dekodér + joint network; vstupy
  `encoder_outputs`, `targets`, `target_length`, `input_states_1/2`; výstupy `outputs`, `prednet_lengths`,
  `output_states_1/2`. Volá se iterativně v greedy decoding smyčce.
- `config.json` – `nemo-conformer-tdt`, `features_size: 128`, `subsampling_factor: 8`.
- `vocab.txt` – slovník tokenů, jeden token na řádek, řádek s `<blk>` definuje blank index.

**Pipeline:**
1. Prepend 250 ms ticha (nulové PCM vzorky) před skutečný audio signál.
2. Preprocessor session: `waveforms` → `features` (log-mel), `features_lens`.
3. Encoder session: `audio_signal=features`, `length=features_lens` → `outputs`, `encoded_lengths`.
4. TDT greedy decoder smyčka: předává `outputs` jako `encoder_outputs`, `targets`, `target_length`
   a `input_states_1/2`; zpracuje `outputs`, `prednet_lengths`, `output_states_1/2` podle Rust source
   `transcribe-rs/src/onnx/parakeet/mod.rs`.
5. Vocabulary lookup: token IDs → text přes `vocab.txt` slovník.

**Co se změní:**
- Vznikne `Services/ParakeetService.cs` implementující aktuální rozhraní `ITranscription`.
- Sessiony se vytvářejí přes `Services/OnnxRuntime/BackendFactory` a app-owned contracts; WPF projekt nepřidává
  přímou ORT binding reference a nesdílí ORT typy přes plugin boundary.
- Provider se řídí uloženým `OrtBackend`; `ActiveBackendInfo` se odvozuje z execution profilu po `Run`
  a předává se spolu s jobem do ETA.
- `StateChanged` event používá stejný `TranscriptionState` enum jako `Whisper`.

---

#### 3.5.4 Výběr a inicializace engine při startu

**Proč:** Po implementaci Parakeet musí aplikace na startu vytvořit transkripční service odpovídající
uloženému `EngineType` a nesmí při chybě tiše přepnout na jiný engine.

**Co se změní:**
- Vznikne engine factory, která podle `AppSettings.EngineType` vytvoří `Whisper` nebo `ParakeetService`.
- `App.OnStartup` inicializuje vybranou implementaci `ITranscription` s kompatibilním modelem a providerem.
- `MainWindow` a transkripční joby používají rozhraní `ITranscription` a zachovají identitu aktivního backendu.
- Výběr při startu neřeší živé přepnutí v Settings. Transakční změna engine za běhu je oddělená Fáze 3.6.

---

### 3.6 Přidat transakční interaktivní výběr engine v Nastavení

**Proč:** Volba engine není totéž co volba execution provideru. Uživatel potřebuje před uložením ověřit,
že vybraný engine lze skutečně inicializovat, a změna nesmí poškodit ani přerušit přijaté transkripce.

**Požadované chování:**
- Po změně engine pozastavit přijímání nových PTT úloh a zobrazit, že probíhá přepnutí; všechny aktivní
  i zařazené transkripce musí bezpečně doběhnout. Již přijaté audio se nesmí zahodit.
- Až po vyprázdnění úloh zkusit inicializovat kandidátní engine s vybraným modelem/providerem a zobrazit
  skutečný výsledek a ověřenou identitu runtime/provideru.
- Tlačítko **Save** povolit až po úspěšném preflightu. Při chybě zůstanou uložené nastavení i aktivní
  engine beze změny; chyba musí být srozumitelná a Save zůstane zakázané.
- Při úspěchu se změna uloží až po potvrzení **Save**. Cancel/zavření vrátí původní výběr a obnoví
  přijímání úloh. Po transakci zachovat pořadí a výsledky již přijatých úloh.
- Preflight nesmí nevratně připnout živý proces k neuloženému ORT native core. Ověřit bezpečnou hranici
  (izolovaný helper proces nebo řízený restart); nespoléhat na unload `AssemblyLoadContext`.
- Pokud aktivace vyžaduje restart, říct to před uložením a nepředstírat aktivní engine před jeho skutečnou
  inicializací. Nenabízet engine bez reálného initializeru; Parakeet z Fáze 3.5 lze nabídnout až po úspěšné inicializaci jeho skutečné service.

**Stav:** Povinná, zatím neimplementovaná. Specifikace a acceptance testy jsou v technické specifikaci Fáze 3.
**Závislost:** Fáze 3.5 dodá skutečné Whisper a Parakeet initializery. Fáze 3.6 ověří jejich bezpečné
přepínání po dokončení všech přijatých transkripcí.

---

## Fáze 4 – dodělat hrubý refactoring

Původní refaktorovací položky jsou v sekci **Dokončeno**. Přechod na Avalonia je odložen do Fáze 7.

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
- Pro Parakeet platí jiné výchozí ticho (250 ms), řeší se v Fázi 3.5 samostatně v `ParakeetService`.

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

## Fáze 7 – Průzkum a případný přechod na Avalonia

**Proč:** Do dokončení Fází 0–6 se projekt soustředí na Windows. Teprve potom se vyhodnotí,
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

### ✅ Fáze 3.0 – Ověřená identita backendu a ETA fingerprint

**Dokončeno:** 11. listopadu 2026.

- DLL probe už nerozhoduje o podpoře, stavu UI ani ETA backendu. Widget zobrazuje `Loading model…` až do
  inicializace a potom používá pouze runtime identity vytvořenou z `RuntimeOptions.LoadedLibrary`.
- `ActiveBackendInfo` nese stabilní engine/runtime/provider IDs, device class `cpu`/`gpu`, seznam providerů
  a volitelnou autoritativní CUDA major version. Neznámý runtime se neklasifikuje ani nezapisuje do ETA.
- Stejný neměnný descriptor se ukládá do každého `TranscriptionJob`, používá se pro ETA estimate i record a
  vstupuje do fingerprintu adaptivní inference capacity.
- SQLite JSON přesunul `backend` a `cudaVersion` do vnořeného objektu. Staré řádky zůstávají zachovány,
  ale jejich provider-ambiguous fingerprints se znovu nepoužijí.
- Ověřeno lokálně: **235/235 Debug testů**, **235/235 Release testů**, oba aplikační buildy s
  **0 warningy a 0 chybami**. Hosted GitHub Actions neběželo.

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

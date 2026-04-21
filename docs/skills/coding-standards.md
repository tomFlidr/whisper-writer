# WhisperWriter – Coding Standards

This document defines all mandatory coding conventions for the WhisperWriter project.
Every contributor and AI assistant must follow these rules without exception.

---

## 1. Language

- All **code**, **comments**, **XML docs**, **commit messages**, and **file edits** must be written in **English**.
- The only exception is `docs/ROADMAP.md`, which is written in Czech (project planning document for the owner).

---

## 2. Base Style – K&R

The entire codebase uses **K&R style** as its foundation. This means:
- Opening brace on the **same line** as the statement – never on a new line (not Allman).
- One space before the opening brace.
- `else`, `catch`, `finally` on the same line as the closing brace.

```csharp
// CORRECT – K&R
public void Foo() {
    if (condition) {
        DoSomething();
    } else {
        DoOther();
    }
    try {
        Risky();
    } catch (Exception ex) {
        LogService.Error("failed", ex);
    }
}

// WRONG – Allman
public void Foo()
{
    if (condition)
    {
        DoSomething();
    }
    else
    {
        DoOther();
    }
}
```

This applies to: classes, methods, properties, if/else, for, foreach, while, try/catch/finally, switch, lambdas, object initializers.

**Additional formatting rules on top of K&R:**
- **No unnecessary blank lines.** Do not add blank lines between logically connected statements, between a field declaration and the next field, or between short related methods. Use one blank line to separate logically distinct sections within a method or class.
- **File encoding:** UTF-8 without BOM (`charset = utf-8`), LF line endings (`\n`), TAB indentation (width 4).
- **No blank line at end of file** (`insert_final_newline = false`).
- **Do not run VS 2022 `Ctrl+K+D`** (Format Document) – it ignores `csharp_new_line_before_open_brace = none` and converts to Allman. Maintain K&R manually.

---

## 3. File and Class Size Limits

- **Methods must not exceed 50 lines** (including blank lines and comments inside the method body). If a method grows beyond this, extract a private helper.
- **Classes must not exceed 500–600 lines** (including all comments, blank lines, and nested types). If a class grows beyond this, split responsibilities into separate classes.
- These limits are not enforced by a tool today but are a hard architectural rule. When reviewing or editing, actively look for opportunities to extract.

---

## 4. Namespaces

Use **file-scoped namespaces** (C# 10+). One namespace per file, no extra indentation level.

```csharp
// CORRECT
namespace WhisperWriter.Services;

public sealed class MyService {
    ...
}

// WRONG – block namespace adds one indentation level
namespace WhisperWriter.Services {
    public sealed class MyService {
        ...
    }
}
```

---

## 5. Naming Conventions

| Element | Convention | Example |
|---|---|---|
| Classes, interfaces | PascalCase | `WhisperService`, `ITranscriptionService` |
| Public methods | PascalCase | `TranscribeAsync`, `SaveFocus` |
| Public properties | PascalCase | `ModelPath`, `HotkeyVkCodes` |
| Protected members | camelCase | `recordedBytes`, `hotkey` |
| Private fields | `_camelCase` (underscore prefix) | `_factory`, `_recorder` |
| Local variables | camelCase | `modelPath`, `wavBytes` |
| Constants (C#) | PascalCase | `MinModelFileSizeBytes` |
| Constants (Win32) | UPPER_CASE | `WM_DISPLAYCHANGE`, `GWL_EXSTYLE` |
| Async methods | Suffix `Async` | `InitializeAsync`, `TranscribeAsync` |
| Interfaces | Prefix `I` | `ITranscriptionService` |
| Event args parameters | `sender`, `e` | standard WPF convention |

**Additional naming rules:**
- **Instance members always use `this.`** – access every instance field, property, and method via `this._field`, `this.Property`, `this.Method()`. Never omit `this` for instance members.
- **Static members always use the class name** – call static methods and access static fields via `ClassName.Member()`, never via an implicit unqualified call or `this`.

```csharp
public sealed class AudioRecorder {
    private bool _recording;
    private static readonly int SampleRate = 16000;

    public void StartRecording() {
        this._recording = true;                    // CORRECT – this. for instance field
        int rate = AudioRecorder.SampleRate;        // CORRECT – ClassName. for static
    }

    // WRONG – omitting this. and class name
    public void Bad() {
        _recording = true;                         // missing this.
        int rate = SampleRate;                     // missing ClassName.
    }
}
```

---

## 6. Braces for conditions

- Always use braces for `if` / `else if` / `else` branches. A branch must have an opening and closing brace even if it contains a single statement.
- Exception: a single-line `if` with no `else` and no `else if` is allowed, but the statement of the branch must be placed on a new indented line and still use the K&R brace style for the `if` itself.

Examples:

```csharp
// CORRECT – single-line branch allowed (no else/else if)
public void MaybeLog() {
    if (condition)
        this.Log();
}

// CORRECT – braces always allowed and preferred
public void DoWork() {
    if (condition) {
        DoSomething();
    } else {
        DoOther();
    }
}

// WRONG – branch without braces when else exists
public void Bad() {
    if (condition)
        DoSomething();
    else
        DoOther();    // NOT allowed: else must have braces
}
```

---

## 7. Method-declaration spacing

- Method declaration spacing: in method (and delegate/interface method) declarations there MUST be a single space between the method name and the opening parenthesis. This rule applies to declarations/definitions only, not to call sites.

Examples:

```csharp
// CORRECT – method definition with space before '('
public void SaveSettings () {
    // ...
}

public interface IWriter {
    void WriteLine (string text);
}

// WRONG – no space before '(' in definition
public void SaveSettings() {
    // ...
}

// NOTE: method calls keep their usual form without enforced space
// call: writer.WriteLine("hi");
```

---

## 8. Nullable and Implicit Usings

- `<Nullable>enable</Nullable>` is on. All reference types must explicitly declare nullability.
- `<ImplicitUsings>enable</ImplicitUsings>` is on. Do not add `using System;`, `using System.Collections.Generic;` etc. unless not included by default.
- Prefer `?` annotation over null-forgiving `!`. Use `!` only when null-safety is guaranteed by invariant; document why with an inline comment.

---

## 9. Class Design

- **Do not use `sealed`** – every class must remain inheritable. Sealing a class removes future flexibility without meaningful benefit at this scale.
- Prefer **`protected`** over `private` for fields and methods. Private visibility should be used only when there is a specific, justified reason (e.g., a backing field that must never be touched directly). When in doubt, choose `protected` so that subclasses can extend behaviour without copy-pasting.
- **`static`** classes only for pure utility helpers with no instance state (`LogService`, `TextInjector`, `VkCodeHelper`).
- No MVVM framework. Direct code-behind in WPF. The app is intentionally small – avoid framework overhead.
- Prefer **constructor injection** over property injection or service locator for dependencies.
- `App.SettingsService`, `App.History`, `App.Eta`, `App.TranscriptionService` are the only allowed global static access points. No other class should reference `App.*` except where architecturally necessary (e.g., `MainWindow`).

---

## 10. Member Order Within a Class

Members must appear in the following order. Within each section, **static members come before instance members**, and within each group the access order is **public → protected → private**.

```
1. Events
   ├── static:   public  →  protected  →  private
   └── instance: public  →  protected  →  private

2. Properties
   ├── static:   public  →  protected  →  private
   └── instance: public  →  protected  →  private

3. Fields
   ├── static:   public  →  protected  →  private
   └── instance: public  →  protected  →  private

4. Constructors
   └── public  →  protected  →  private

5. Methods
   ├── static:   public  →  protected  →  private
   └── instance: public  →  protected  →  private
```

Example skeleton:

```csharp
public class MyService {
    public static event EventHandler? GlobalStateChanged;
    public event EventHandler<string>? StateChanged;

    public static string DefaultName { get; } = "default";
    public string Name { get; protected set; } = string.Empty;

    protected static readonly int MaxRetries = 3;
    protected bool _initialized;

    public MyService() {
    }

    public static MyService Create() => new MyService();
    public void Initialize() { }
    protected void OnStateChanged(string state) { }
}
```

---

## 11. Async / Await

- Use `async/await` for every I/O operation: file access, model loading, transcription, HTTP downloads.
- Never use `.Result` or `.Wait()` on a `Task` from a synchronous context – risks deadlock on the UI thread.
- Never use `ContinueWith` + `Unwrap` when a simple `await` will do. Reserve `ContinueWith` only when continuation options (e.g., `TaskContinuationOptions.OnlyOnFaulted`) are genuinely needed.
- Fire-and-forget tasks must be assigned to `_` and handle errors internally via `StateChanged` or try/catch.

```csharp
// CORRECT – fire and forget, errors handled inside
_ = this._transcriptionService.InitializeAsync(modelPath);

// WRONG – exception is swallowed
Task.Run(() => this._transcriptionService.InitializeAsync(modelPath));
```

---

## 12. Thread Safety and UI Access

- All WPF UI property changes must happen on the UI thread. Use `Dispatcher.Invoke` (sync) or `Dispatcher.BeginInvoke` (async) from background threads.
- `TextInjector.InjectText()` **must** run on a **background thread** (`Task.Run`) – it calls `Thread.Sleep`.
- `TextInjector.RestoreFocus()` **must** run on the **UI thread** – `AttachThreadInput` requires a message queue.
- `HotkeyService` events are raised on a background thread – handlers must marshal to the UI thread explicitly.

---

## 13. Win32 P/Invoke

- All P/Invoke declarations must be **private**, grouped in a `#region Win32` block or a nested `private static class NativeMethods`.
- P/Invoke must not leak outside the class that owns it. `TextInjector` owns `SendInput`/`GetAsyncKeyState`; `HotkeyService` owns its own polling copy; `MainWindow` owns window-style calls.
- Document every non-obvious flag or field offset with an inline comment.
- `INPUT` struct on 64-bit Windows: union field (`ki`/`mi`) at `[FieldOffset(8)]`. `sizeof(INPUT)` = 40 bytes (64-bit) / 28 bytes (32-bit).

---

## 14. Error Handling

- All `catch` blocks must log via `LogService.Error(message, exception)` or `LogService.Warning(...)`.
- Never swallow exceptions silently – empty `catch` blocks are forbidden.
- Service-level errors must surface via a `StateChanged` event so the UI can show an error message.
- Background tasks that cannot use events must fall back to `.ContinueWith(t => LogService.Error(..., t.Exception?.InnerException))`.

---

## 15. Logging

- `LogService` is a static facade over Serilog.
- **All logging is DEBUG-only.** In Release, all methods are no-ops (`#if DEBUG`).
- Methods: `LogService.Info(string)`, `LogService.Warning(string, Exception?)`, `LogService.Error(string, Exception?)`, `LogService.Transcription(string, TimeSpan)`.
- Do not reference `Serilog` directly anywhere except `LogService`.


---

## 16. One Type Per File

- Every file contains exactly **one** top-level type: one class, one interface, one enum, or one struct. Never put multiple types in a single file.
- If a file currently contains more than one type, refactor it – split each type into its own file.

### File placement rules

**Classes** go in the folder that matches their role (`Services/`, `Util/`, `Views/`).

**Interfaces** are placed as close as possible to where they are implemented or primarily used:
- Next to the implementing class (same folder) – preferred when the interface is specific to one class or one area.
- In the folder where it is most commonly implemented (e.g. `Services/` for `ITranscriptionService`).
- In `Util/` only if the interface is truly application-wide and has no single natural home.

**Enums** – if there are multiple enums, group them in `Util/Enums/`.

**Structs** – if there are multiple structs, group them in `Util/Structs/`.

> Do not create a `Util/Interfaces/` folder unless the project accumulates a large number of genuinely cross-cutting interfaces with no better home.

---

## 17. Editing Files
- **Always** use `replace_string_in_file` or `multi_replace_string_in_file` to edit existing files.
- Never use PowerShell or Node.js to **write or overwrite** C# source files.
- Exception: non-source files (`.md`, `.editorconfig`, `.ps1`) may be written via terminal.
- When using PowerShell to **read** files, always add `-Encoding UTF8`.
- After every completed task with a successful build, update `copilot-instructions.md` and `docs/ROADMAP.md`.

---

## 18. No Section-Separator Comments

Do **not** use visual separator comments of any form to divide members into sections within a class. Examples of **forbidden** patterns:

```csharp
// ── Events ────────────────────────────────────────────────────────────────
// ─────────────────────────── Fields ──────────────────────────────────────
// ==== Public API ==========================================================
// ### Helpers ###
```

These decorative lines add noise without providing information that cannot be inferred from member types and XML `<summary>` docs. Use XML doc comments (`/// <summary>`) on individual members instead.

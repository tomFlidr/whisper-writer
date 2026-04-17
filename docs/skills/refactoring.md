# Skill: Refactoring

Follow this checklist when refactoring existing code in WhisperWriter.

---

## 1. Build before you start

Confirm the codebase is clean before touching anything:

```powershell
dotnet build "D:\tec\cs\whisper-writer\WhisperWriter.csproj" -c Debug --no-restore
```

Do not start refactoring if the build is already broken – fix it first.

---

## 2. Plan the change

- Read the relevant files before editing. Understand the full call chain.
- If renaming a type, method, or property: use the Grep tool to find **all usages** across the codebase first.
- If splitting a class: identify which members belong to each new class before writing any code.
- Check `docs/skills/coding-standards.md` – especially member order, naming, and access modifier rules.

---

## 3. Make the change

- Edit one logical unit at a time (one class, one rename, one extraction).
- Follow `docs/skills/coding-standards.md` without exception:
  - K&R braces, TAB indentation, LF line endings.
  - Member order: events → properties → fields → constructors → methods.
  - Prefer `protected` over `private`. No `sealed`.
  - `this.` for instance members, `ClassName.` for static members.
- Do not change behaviour – refactoring must be invisible to the user.

---

## 4. Build after every logical step

```powershell
dotnet build "D:\tec\cs\whisper-writer\WhisperWriter.csproj" -c Debug --no-restore
```

Fix all errors and warnings before continuing to the next step.

---

## 5. Update documentation

After a successful build, update all docs per `docs/skills/commit.md` – Section 2.

Key things to check for refactoring:
- `.github/copilot-instructions.md` Section 3 (project structure) – if files were added, removed, or moved.
- `.github/copilot-instructions.md` Section 4 (key files) – if class responsibilities changed.
- `docs/skills/coding-standards.md` – if a new pattern or convention emerged from the refactoring.

---

## 6. Commit

Follow `docs/skills/commit.md`. Use commit type `refactor`:
```
refactor: extract WindowPositioner from MainWindow
```
# Skill: New Feature

Follow this checklist when implementing a new feature in WhisperWriter.

Production source paths below are relative to `WhisperWriter/`; test files are in the sibling
`WhisperWriter.Tests/` project. Commands run from the repository root.

---

## 1. Understand the scope

- Read the relevant item in `.agents/ROADMAP.md`.
- Read the phase specification in `.agents/specs/` and `.agents/skills/testing.md`.
- Keep `.agents/specs/phase-0-coverage-inventory.md` current when public/protected behavior or
  XAML event bindings change. For new behavior, write and run the failing acceptance test first.
- Read all affected files and use `search_files` to find related code and usages.
- If the feature touches settings, use `WhisperWriter/Models/AppSettings.cs` and the current source/tests as the
  authority; the older `AGENTS.md` implementation notes may contain stale paths.
- If the feature adds a service, use `.agents/skills/coding-standards.md` Section 16 (File placement rules).

---

## 2. Build before you start

```powershell
dotnet build "WhisperWriter/WhisperWriter.csproj" -c Debug --no-restore
```

Do not start if the build is already broken.

---

## 3. Implement

- Follow `.agents/skills/coding-standards.md` without exception.
- New classes go in the correct folder:
  - `WhisperWriter/Services/` – stateful classes with side effects.
  - `WhisperWriter/Models/` – plain data types such as `AppSettings` and `TranscriptionEntry`.
  - `WhisperWriter/Utils/` – enums, shared interfaces, and static helpers.
  - `WhisperWriter/Views/` – WPF windows and panels only, no business logic.
- New user-configurable values go in `WhisperWriter/Models/AppSettings.cs` as public properties with defaults.
- New UI strings visible to the user must be in English.
- If the feature requires a new NuGet package, ask the user before adding it.

---

## 4. Build after every logical step

```powershell
dotnet build "WhisperWriter/WhisperWriter.csproj" -c Debug --no-restore
```

Fix all errors and warnings before continuing.

---

## 5. Update documentation

After a successful build, update all docs per `.agents/skills/commit.md` – Section 2.

Key things to check for new features:
- Use `AGENTS.md` Sections 3–4 for the current map; Appendix A contains historical notes.
- `.agents/specs/phase-0-coverage-inventory.md` – update affected behavior/XAML mappings.
- `AGENTS.md` Section 7 – remove a resolved issue from open tasks if it was listed there.
- `AGENTS.md` Section 10 – remove the idea if it was listed there.
- `README.md` – document any new user-visible behaviour.

---

## 6. Commit

Follow `.agents/skills/commit.md`. Use commit type `feat`:
```
feat: add silent recording discard below RMS threshold
```
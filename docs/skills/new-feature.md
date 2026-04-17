# Skill: New Feature

Follow this checklist when implementing a new feature in WhisperWriter.

---

## 1. Understand the scope

- Read the relevant item in `docs/ROADMAP.md`.
- Read all files that will be affected. Use Grep to find related code.
- If the feature touches `AppSettings`, check `.github/copilot-instructions.md` Section 9 (Settings and Configuration).
- If the feature adds a new service, check `docs/skills/coding-standards.md` Section 14 (Project Structure) for where it belongs.

---

## 2. Build before you start

```powershell
dotnet build "D:\tec\cs\whisper-writer\WhisperWriter.csproj" -c Debug --no-restore
```

Do not start if the build is already broken.

---

## 3. Implement

- Follow `docs/skills/coding-standards.md` without exception.
- New classes go in the correct folder:
  - `Services/` – stateful classes with side effects.
  - `Util/` – plain data types, enums, static helpers.
  - `Views/` – WPF windows and panels only, no business logic.
- New user-configurable values go in `Util/AppSettings.cs` as public properties with defaults.
- New UI strings visible to the user must be in English.
- If the feature requires a new NuGet package, ask the user before adding it.

---

## 4. Build after every logical step

```powershell
dotnet build "D:\tec\cs\whisper-writer\WhisperWriter.csproj" -c Debug --no-restore
```

Fix all errors and warnings before continuing.

---

## 5. Update documentation

After a successful build, update all docs per `docs/skills/commit.md` – Section 2.

Key things to check for new features:
- `.github/copilot-instructions.md` Section 3 – add new files to the project structure.
- `.github/copilot-instructions.md` Section 4 – add a description for each new file.
- `.github/copilot-instructions.md` Section 7 – remove the feature from open tasks if it was listed there.
- `.github/copilot-instructions.md` Section 10 – remove the idea if it was listed there.
- `README.md` – document any new user-visible behaviour.

---

## 6. Commit

Follow `docs/skills/commit.md`. Use commit type `feat`:
```
feat: add silent recording discard below RMS threshold
```
# Skill: Commit

Follow this checklist every time you create a git commit in WhisperWriter.

---

## 1. Verify the build passes

```powershell
dotnet build "D:\tec\cs\whisper-writer\WhisperWriter.csproj" -c Debug --no-restore
```

Do not commit if the build fails.

---

## 2. Update documentation

After every completed task with a successful build, update **all** of the following files:

### `copilot-instructions.md`
- **Section 3 – Project structure**: add or remove files that changed.
- **Section 4 – Key files**: update descriptions of changed files; add descriptions of new files.
- **Section 7 – Known issues / TODO**: move resolved items to "Resolved issues"; update or remove items that changed; add new open tasks if any arose.
- **Section 10 – Ideas**: remove ideas that have been implemented.

### `docs/ROADMAP.md`
- Move the completed item(s) to the **Dokončeno** section at the bottom.
- Do not add new items without explicit instruction from the user.

### `README.md`
- Update if the changes affect user-visible behaviour: installation steps, settings, model list, keyboard shortcuts, tray menu, etc.

---

## 3. Stage and commit

- Stage only files relevant to the completed task.
- Do not stage `.pfx`, `.snk`, model `.bin` files, or anything in `.gitignore`.

### Commit message format

```
<type>: <short summary in imperative mood>

<optional body: what changed and why, wrap at 72 chars>
```

**Types:**

| Type | When to use |
|---|---|
| `feat` | New user-visible feature |
| `fix` | Bug fix |
| `refactor` | Code change with no behaviour change |
| `docs` | Documentation only |
| `chore` | Build scripts, project files, tooling |
| `style` | Formatting, whitespace (no logic change) |

**Rules:**
- Summary line: max 72 characters, imperative mood ("add", "fix", "remove" – not "added", "fixed").
- No period at the end of the summary line.
- Body is optional; use it when the reason for the change is not obvious.
- English only.

**Examples:**
```
feat: add configurable push-to-talk hotkey

refactor: move AppSettings and related types from Models/ to Util/

fix: correct INPUT struct field offset to 8 on 64-bit Windows

docs: update copilot-instructions with EtaStatsService schema
```
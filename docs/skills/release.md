# Skill: Release

Follow this checklist when creating a new release of WhisperWriter.

---

## 1. Decide the version number

WhisperWriter uses **Semantic Versioning**: `MAJOR.MINOR.PATCH.0`

| Increment | When |
|---|---|
| `PATCH` | Bug fixes, no new features |
| `MINOR` | New features, backwards compatible |
| `MAJOR` | Breaking changes or major redesign |

The fourth segment is always `0` (assembly version convention).

---

## 2. Bump the version

Update the version in **two places**:

### `WhisperWriter.csproj`
```xml
<Version>1.2.0</Version>
<AssemblyVersion>1.2.0.0</AssemblyVersion>
<FileVersion>1.2.0.0</FileVersion>
```

### `copilot-instructions.md` – Section 2 (Environment and stack)
```
| Version | 1.2.0.0 |
```

---

## 3. Update release notes

In `docs/ROADMAP.md`, ensure all completed items are in the **Dokončeno** section.
The GitHub Actions workflow generates release notes automatically from commit messages
(`generate_release_notes: true`) – so meaningful commit messages matter.

---

## 4. Commit the version bump

Follow `docs/skills/commit.md`. Use commit type `chore`:
```
chore: bump version to 1.2.0
```

---

## 5. Push the tag

```powershell
git tag v1.2.0
git push origin v1.2.0
```

The tag triggers `.github/workflows/release.yml` which:
1. Builds `win-x64` (with CUDA) and `win-x86` (CPU only).
2. Strips `.pdb` files.
3. Packs `WhisperWriter-v1.2.0-win-x64.zip` and `WhisperWriter-v1.2.0-win-x86.zip`.
4. Creates a GitHub Release with auto-generated notes by commits since the last release.

> Tags containing `-` (e.g. `v1.2.0-beta`) are automatically marked as pre-release.

---

## 6. Verify the release

- Check GitHub Actions for build success.
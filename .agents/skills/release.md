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

### `WhisperWriter/WhisperWriter.csproj`
```xml
<Version>1.4.0</Version>
<AssemblyVersion>1.4.0.0</AssemblyVersion>
<FileVersion>1.4.0.0</FileVersion>
```

### `AGENTS.md` – Section 2 (Environment and stack)
```
| Version | 1.4.0.0 |
```

---

## 3. Update release notes

In `.agents/ROADMAP.md`, ensure all completed items are in the **Dokončeno** section.
The GitHub Actions workflow generates release notes automatically from commit messages
(`generate_release_notes: true`) – so meaningful commit messages matter.

---

## 4. Commit the version bump

Follow `.agents/skills/commit.md`. Use commit type `chore`:
```
chore: bump version to 1.4.0
```

---

## 5. Push the tag

```powershell
git tag v1.4.0
git push origin v1.4.0
```

The tag triggers `.github/workflows/release.yml`, which is configured to:
1. Build `win-x64` with the Parakeet CPU ORT pack and `win-x86` without ORT provider packs.
2. Build the DirectML provider ZIP for release and build the CUDA pack as a short-lived CI artifact.
   The CUDA ZIP is not attached to v1.4.0 because runtime provisioning is still pending.
3. Strip `.pdb` files and package the two application ZIPs.
4. Create a GitHub Release and attach both app ZIPs and the DirectML ZIP with auto-generated notes.

The ONNX CUDA provider pack requires compatible CUDA 13/cuDNN 9 runtime DLLs and is not a user-facing
v1.4.0 asset. `OrtCudaRuntimePath` is not a substitute for a complete provisioning solution. Hosted Release
workflow [run #9](https://github.com/tomFlidr/whisper-writer/actions/runs/43341305308) succeeded on 2026-11-13
and published [v1.4.0](https://github.com/tomFlidr/whisper-writer/releases/tag/v1.4.0) with the two app ZIPs and
DirectML ZIP; the CUDA provider pack built successfully as a CI artifact but was not attached. See
`.agents/phase-3-progress.md`.

> Tags containing `-` (e.g. `v1.4.0-beta`) are automatically marked as pre-release.

---

## 6. Verify the release

- Check GitHub Actions for build success.
- Verify the release includes both application ZIPs and the DirectML provider ZIP.
- Verify the CUDA provider ZIP is absent from v1.4.0 release assets.
# WhisperWriter – Skills Index

This file is the entry point for all skill documents.
Load the relevant skill file before performing the corresponding action.

| Action | Skill file | When to load |
|---|---|---|
| Writing or reviewing any code | `.agents/skills/coding-standards.md` | Always – before writing or reviewing code |
| Tests and behavior changes | `.agents/skills/testing.md` | Before tests, features, fixes, or refactors |
| Creating a git commit | `.agents/skills/commit.md` | Before every commit |
| Creating a release | `.agents/skills/release.md` | Before bumping version or pushing a tag |
| Refactoring existing code | `.agents/skills/refactoring.md` | Before any refactoring task |
| Implementing a new feature | `.agents/skills/new-feature.md` | Before starting new functionality |
| Investigating a bug or crash | `.agents/skills/debugging.md` | When diagnosing unexpected behaviour |

> Skills may reference each other. For example, finishing a new feature requires
> following the commit skill afterwards.
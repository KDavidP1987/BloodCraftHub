# BloodCraftHub — Versioning policy

## TL;DR

- One source of truth: `<Version>` in `BloodCraftHub/BloodCraftHub.csproj`.
- `BloodCraftHub/thunderstore.toml` `versionNumber` must equal it (the preflight check enforces this).
- Three-part semver `MAJOR.MINOR.PATCH`. No `-pre`/`+build` suffixes — Thunderstore doesn't accept them on the manifest.
- Bump on **every** Thunderstore release. Re-uploading the same version is rejected by Thunderstore.

## What each segment means here

| Segment | Bump when |
|---|---|
| **MAJOR** | A backwards-incompatible UX change OR a hard cutover to a new Bloodcraft major version that breaks the protocol. Example: dropping the Bloodcraft `v1.x` chat-regex pipeline entirely. |
| **MINOR** | New feature surface (new panel, new overlay, new supported command group). Players notice it. |
| **PATCH** | Bug fixes, performance, internal refactors, dependency-only bumps, doc/icon updates. Players ideally don't notice anything except the bug being gone. |

We are pre-1.0 right now (`0.x.y`), so the rules are looser: MINOR bumps may include breaking changes until we ship `1.0.0`.

## Bloodcraft compatibility coupling

The mod's version is independent of Bloodcraft's, **but** every release is implicitly pinned to a tested Bloodcraft server version. Track the tested-against version in:

1. `CHANGELOG.md` — call it out per release entry.
2. `README.md` — single "tested against Bloodcraft `X.Y.Z`" line near the top.
3. `docs/MOD_DESIGN.md` — versions-of-dependency table.

When Bloodcraft ships a breaking change, expect a PATCH bump just to retest, or a MAJOR/MINOR bump if we have to change behavior.

## Bumping versions (single command, see `tools/bump-version.ps1`)

The bump script edits `csproj`, `thunderstore.toml`, and prepends a stub entry to `CHANGELOG.md` in one shot. **Always use it** — manual edits across three files drift.

```powershell
# From the BloodCraftHub repo root:
.\tools\bump-version.ps1 -To 0.2.0
```

The script:
1. Validates the target version is a valid 3-part semver and is strictly greater than the current one.
2. Updates `<Version>` in `BloodCraftHub/BloodCraftHub.csproj`.
3. Updates `versionNumber` in `BloodCraftHub/thunderstore.toml`.
4. Prepends `## <new> — TODO\n\n- TODO entries\n\n` to `CHANGELOG.md`.
5. Stages those three files (does not commit — review first).

## When not to bump

- Working on a feature branch / draft commit. Bumping is the **last** thing you do before pushing a release.
- Doc-only changes that aren't part of an upcoming release.
- WIP commits — version stays at the unreleased number until preflight runs cleanly.

## Pre-release reservations

We don't ship `-alpha` / `-beta` suffixes (Thunderstore manifest rejects them). For private/testing builds, use a high-PATCH like `0.2.99` with `IS_TESTING = true`, but **never upload that to Thunderstore.** The preflight check refuses to package when `IS_TESTING == true`.

## What the preflight check verifies for versions

See [`PREFLIGHT.md`](PREFLIGHT.md). Specifically:

- csproj version ≡ thunderstore.toml version
- Both versions are valid `<int>.<int>.<int>` semver
- The current version has a `## <version>` header at the top of `CHANGELOG.md`
- The git working tree is clean (no uncommitted changes to those three files when packaging)

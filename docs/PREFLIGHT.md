# BloodCraftHub — Preflight check

The preflight is the gate between "working in dev" and "shipping a release." It's both **a checklist for humans** and **a script for machines** (`tools/preflight.ps1`). The script enforces the checks the pre-commit git hook runs on every commit.

## When the preflight runs

| Stage | Trigger | What runs | Fails how |
|---|---|---|---|
| **Per commit** | `git commit` (via `.githooks/pre-commit`) | Fast checks: version sync, no `IS_TESTING = true`, no real-looking HMAC key, no merge conflict markers. | Aborts the commit with a clear message. Bypass: `git commit --no-verify` (don't, unless you know why). |
| **Per release** | Run by hand before packaging | All of the above PLUS: clean build, CHANGELOG header present, icon dimensions, manifest validates, no uncommitted changes. | Exits non-zero. Don't upload until clean. |
| **CI (future)** | When/if a GitHub repo + Actions are added | Same as per-release. | Blocks the PR / release workflow. |

## How to run it

```powershell
# Fast subset (the git hook runs this):
.\tools\preflight.ps1

# Full subset (run before packaging for Thunderstore):
.\tools\preflight.ps1 -Mode Release
```

Exit code `0` means clean. Anything else means at least one check failed; the script prints which.

## Checklist — what each check enforces and why

### Code health (commit + release)

- [ ] **Version sync.** `<Version>` in `BloodCraftHub/BloodCraftHub.csproj` equals `versionNumber` in `BloodCraftHub/thunderstore.toml`. *Why: any drift means manifest.json and the published version disagree — confusing at best, "where's my update?" at worst.*
- [ ] **No `IS_TESTING = true`** in `Plugin.cs`. *Why: surfacing dummy panels in a shipped mod was the v1.x BloodCraftUI worry — the const exists, the check defends it.*
- [ ] **No real HMAC key in `Resources/secrets.json`.** The check refuses any value that looks like base64 of ≥16 bytes — the placeholder file shipped with the repo has an empty string. *Why: the shared key is server-distributed and must never be committed.*
- [ ] **No merge conflict markers** (`<<<<<<<`, `=======`, `>>>>>>>`) in tracked files. *Why: an unfinished merge slipping in is silent.*
- [ ] **No `Console.WriteLine` / `Debug.Log` in committed code** (warn, not fail). *Why: BepInEx plugins should use `Plugin.LogInstance` / `Core.Log` — game console pollution is rude.*

### Release-only

- [ ] **`CHANGELOG.md` has a `## <current-version>` header at the top.** *Why: every release needs notes; Thunderstore surfaces them on the package page.*
- [ ] **`icon.png` exists at `BloodCraftHub/icon.png` and is exactly 256×256.** *Why: Thunderstore rejects any other size.* (Released builds copy this into the zip.)
- [ ] **`dotnet build -c Release` succeeds with zero warnings** (or no new warnings since the last release — the script logs the count, doesn't block).
- [ ] **Generated `manifest.json` validates against the Thunderstore schema.** The script runs the validator rules locally; you can also paste it into https://thunderstore.io/tools/manifest-v1-validator/ as a second check.
- [ ] **Git working tree is clean** on the version-bearing files (`csproj`, `thunderstore.toml`, `CHANGELOG.md`). *Why: shipping uncommitted changes is the #1 way version drift gets reintroduced.*
- [ ] **Dependency versions in `manifest.json`/`thunderstore.toml` exist on Thunderstore.** Currently just `BepInEx-BepInExPack_V_Rising-1.733.2` — bump the script when we add more.

## When a check fails

The script tells you which one and why. The fix is almost always:

- Version drift → re-run `.\tools\bump-version.ps1` (single source of truth).
- `IS_TESTING = true` → flip it back to `false` in `Plugin.cs`. *Always.* If you need the test UI in a dev session, set it true, work, and **revert before committing.**
- Real key in `secrets.json` → restore the placeholder. Use `Resources/secrets.local.json` (gitignored) for local dev with a real key.
- Missing CHANGELOG entry → add one.
- Icon wrong size → resize. Don't trust file-explorer thumbnails; the script reads PNG header bytes.

## The audit trail

Together these produce three layers of audit:

1. **Per-commit git hook log** — every commit is preflight-clean by construction.
2. **`CHANGELOG.md`** — every released version has a human-written summary.
3. **Git history with conventional commit messages** (see [`CONTRIBUTING.md`](../CONTRIBUTING.md)) — `feat:` / `fix:` / `chore:` / `docs:` prefixes make it scannable.

The combination means a future maintainer (or your future self) can reconstruct *what* changed, *why*, and *when* without having to read every diff.

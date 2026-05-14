# Contributing to BloodCraftHub

Workflow conventions for developing the mod. Short version: keep commits small, run the preflight, bump versions through the script.

## One-time setup

```powershell
# After cloning:
.\tools\install-hooks.ps1
```

This sets `git config core.hooksPath .githooks`, so:

- `pre-commit` runs `tools/preflight.ps1 -Mode Commit` — fails the commit on version drift, `IS_TESTING = true`, leaked HMAC keys, merge-conflict markers.
- `commit-msg` enforces Conventional-Commits-lite subjects.

You need PowerShell 7+ (`pwsh`) on PATH for the hooks to fire.

## Day-to-day loop

```powershell
# 1. Make changes.
# 2. Stage them.
git add -p

# 3. Commit. The hooks run.
git commit -m "feat(ui): add familiar stats panel scaffold"
```

If a hook blocks you, **fix the cause** before committing. `--no-verify` is a one-off escape hatch, not a habit.

## Commit message format

`type(scope)?: subject`

| Type | When |
|---|---|
| `feat` | New user-visible capability. |
| `fix` | Bug fix. Include the bug behavior in the body. |
| `chore` | Build infra, tooling, deps. `chore(release): vX.Y.Z` is the canonical release commit. |
| `docs` | `*.md` only. |
| `refactor` | Code shape changed, behavior didn't. |
| `test` | Tests added/changed (we don't have any yet — placeholder type). |
| `build` | csproj/manifest/MSBuild changes. |
| `ci` | If/when we add GitHub Actions. |
| `perf` | Provable perf change. |
| `style` | Formatting only. Rare. |
| `revert` | Reverting a prior commit; include the reverted SHA. |

Scope is optional but encouraged (`ui`, `protocol`, `config`, `patches`, `build`, etc.). Keep the subject ≤100 chars.

## Releases

1. Finish all feature/fix work on `main`.
2. Bump the version with the script (do not hand-edit):
   ```powershell
   .\tools\bump-version.ps1 -To 0.2.0
   ```
3. Edit `CHANGELOG.md` to replace the `TODO` stub with real notes.
4. Run the full preflight:
   ```powershell
   .\tools\preflight.ps1 -Mode Release
   ```
5. Commit:
   ```powershell
   git commit -m "chore(release): v0.2.0"
   ```
6. Tag and push (when remote exists):
   ```powershell
   git tag -a v0.2.0 -m "v0.2.0"
   git push --tags
   ```
7. Build + package for Thunderstore — see [`docs/THUNDERSTORE.md`](docs/THUNDERSTORE.md).

## Auditing — how the dev structure produces a trail

| Artifact | Purpose |
|---|---|
| Per-commit `pre-commit` hook | Every commit is preflight-clean by construction. |
| Conventional commits | Git log scans cleanly: `git log --oneline --grep '^feat'`. |
| `CHANGELOG.md` | Human-curated, per-version release notes. |
| `BloodCraftHub/BloodCraftHub.csproj` `<Version>` | Single source of truth. Bumped only by `tools/bump-version.ps1`. |

You don't need a separate audit log — the combination above answers *what changed, why, and when* for every commit.

## When you want to break the rules

There are legitimate reasons to bypass:

- **Long-running rebase / surgery:** preflight may complain mid-flow. `--no-verify` per commit, run the preflight at the end of the surgery, force a clean state.
- **Test session with `IS_TESTING = true`:** flip it true, do not commit, flip it back. The hook catches the slip.
- **Real HMAC key needed locally:** copy `Resources/secrets.json` to `Resources/secrets.local.json` (gitignored) and load from that path during dev. Do not change the committed file.

If you find yourself bypassing the hooks more than once a week, the hooks are wrong — open an issue / fix the script, don't normalize the bypass.

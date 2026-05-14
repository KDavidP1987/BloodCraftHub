# Publishing BloodCraftHub to Thunderstore.io

Thunderstore is the canonical mod repository for V Rising — it hosts the package, surfaces it to the in-game mod manager (`r2modman` / `Thunderstore Mod Manager`), and handles dependency resolution. This doc captures the rules our package has to satisfy and the two ways we can publish.

**Primary references:**
- Package creation wiki: https://wiki.thunderstore.io/mods/creating-a-package
- Manifest validator: https://thunderstore.io/tools/manifest-v1-validator/
- Thunderstore API (Swagger): https://thunderstore.io/api/docs/
- Thunderstore platform source (open source): https://github.com/thunderstore-io/Thunderstore

## What a valid Thunderstore package looks like

Three files, **at the zip root** (not nested inside a folder):

| File | Rule |
|---|---|
| `manifest.json` | Schema below. Validate at the link above before uploading. |
| `README.md` | UTF-8 encoded. Renders on the package page. |
| `icon.png` | **Exactly 256×256, PNG.** APNG technically works but only the first frame displays. Transparency OK; a visible border helps across themes. |

Plus the mod itself — the BepInEx plugin DLL and any embedded assets — also at the zip root, alongside the three required files.

> Common foot-gun: if you "Send to compressed folder" on the project directory in Explorer, Windows nests everything one level deep. Thunderstore will reject it. Select the files individually, or use the build script described below.

Soft size limit: ~5 GB. Not a constraint for us.

## manifest.json schema

| Field | Rules | Our value |
|---|---|---|
| `name` | Up to 128 chars, `[a-zA-Z0-9_]` only. Underscores render as spaces in the UI. | `BloodCraftHub` |
| `version_number` | Three-part semver `Major.Minor.Patch`. Each component is treated **lexicographically per number**, so `1.0.10` is later than `1.0.2`. | starts at `0.1.0`, drives off `<Version>` in `BloodCraftHub.csproj` |
| `description` | 250 character max — short blurb shown in package lists. | set in `BloodCraftHub.csproj` `<Description>` |
| `website_url` | URL string. **An empty string is required if you don't have a URL** — the field must be present. | empty until repo is published |
| `dependencies` | Array of strings `<team>-<package>-<version>`. Include every Thunderstore package the mod depends on at the exact version you tested against. | `["BepInEx-BepInExPack_V_Rising-1.733.2"]` |

We generate `manifest.json` automatically via MSBuild — `BloodCraftHub/Manifest.props` writes it during `BeforeBuild` from the `<AssemblyName>`, `<Version>`, `<Description>`, and `<PackageProjectUrl>` properties. **You don't hand-edit `manifest.json`** — update the csproj and rebuild.

To add or pin a new Thunderstore dependency, edit the `"dependencies"` array inside `Manifest.props` (it's the only thing in there that isn't auto-derived).

## Publishing flow (v0.8.1+)

The whole "build + stage + zip + verify" sequence is automated by `tools/package-release.ps1`. Upload is the only manual step (Thunderstore requires a logged-in browser session for the publish form).

```powershell
# From the BloodCraftHub repo root:
.\tools\bump-version.ps1 -To 0.9.0     # edits csproj + thunderstore.toml + CHANGELOG stub
# (manually edit CHANGELOG.md to replace the TODO with real notes)

.\tools\package-release.ps1            # runs preflight Release-mode, builds, stages, zips
# → produces dist/BloodCraftHub-<version>.zip with files-at-root layout

# Switches:
#   -SkipPreflight   bypass the preflight (use only if you've already reviewed the failures)
#   -SkipBuild       reuse existing bin/Release/net6.0/ output
#   -Force           overwrite an existing dist zip
```

Then upload at https://thunderstore.io/c/v-rising/create/ — drag the zip on the page, the form auto-fills from `manifest.json`, click publish.

For replicating the icon (vampire+UI theme, generated programmatically):

```powershell
.\tools\generate-icon.ps1   # writes BloodCraftHub/icon.png — re-run after any design change
```

### Manual fallback (if package-release.ps1 fails)

1. `dotnet build BloodCraftHub\BloodCraftHub.csproj -c Release`
2. Stage these files into a single folder, FILES AT ROOT (no enclosing folder):
   - `BloodCraftHub\bin\Release\net6.0\BloodCraftHub.dll`
   - `BloodCraftHub\obj\Release\net6.0\manifest.json` (auto-generated)
   - `BloodCraftHub\icon.png`
   - `README.md`, `LICENSE.txt`, `CHANGELOG.md`
3. Zip the **contents** of that folder (Ctrl+A inside the folder, right-click → compress) — NOT the folder itself.
4. Optional: validate at https://thunderstore.io/tools/manifest-v1-validator/
5. Upload.

### `tcli` (Thunderstore CLI) — alternative for CI/CD

`tcli` reads `thunderstore.toml` and handles zip assembly + token-authenticated upload — useful if we ever wire CI publishing. Service-account tokens at https://thunderstore.io/settings/teams/. We're not using it today; `package-release.ps1` covers the local workflow.

## Pre-publish prereqs (all done as of v0.8.1)

- [x] `icon.png` at `BloodCraftHub/icon.png`, 256×256 — generated by `tools/generate-icon.ps1`. Vampire fangs flanking a UI panel, blood drips at the tips.
- [x] `<PackageProjectUrl>` in `BloodCraftHub.csproj` set to https://github.com/KDavidP1987/BloodCraftHub — populates `manifest.json`'s `website_url`.
- [x] Thunderstore team `kdpen` claimed.
- [x] License: MIT (`LICENSE.txt`) with third-party attribution.
- [x] First public release shipped — see https://thunderstore.io/c/v-rising/p/kdpen/BloodCraftHub/ and https://github.com/KDavidP1987/BloodCraftHub/releases/tag/v0.8.1.

## Version-bump checklist (every release)

1. `.\tools\bump-version.ps1 -To X.Y.Z` (edits csproj + thunderstore.toml + adds a CHANGELOG stub)
2. Edit `CHANGELOG.md` — replace the `TODO` line with real notes
3. `.\tools\preflight.ps1 -Mode Release` and address failures (uncommitted version-bearing files is the usual one — commit the bump first)
4. `.\tools\package-release.ps1` (uses preflight by default; skip with `-SkipPreflight` if you've already reviewed)
5. Commit, tag (`git tag -a vX.Y.Z`), push, then `gh release create vX.Y.Z dist/BloodCraftHub-X.Y.Z.zip --title "..." --notes "..."`
6. Upload the zip at https://thunderstore.io/c/v-rising/create/

Thunderstore does **not** allow re-uploading the same version number; each upload is immutable. If the upload fails for any reason, bump the patch and try again.

## Failure modes worth knowing

- **"Invalid name"** — non-allowed character in `name` (underscores OK, hyphens and dots not).
- **"Files not at root"** — fix the zipping step (see foot-gun above).
- **"Dependency not found"** — the team-package-version triple has to exactly match an existing Thunderstore package. Search the V Rising community page for the BepInExPack version you're pinning.
- **Icon rejected** — must be 256×256 *exactly*. Resize with any image tool; don't trust the OS preview to confirm.
- **Manifest JSON error** — usually a missing `website_url: ""`. The field is mandatory even when empty.

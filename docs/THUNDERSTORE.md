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

## Two ways to publish

### Option A — web upload (simplest, good for the first release)

1. `dotnet build BloodCraftHub\BloodCraftHub.csproj -c Release`
2. Locate the build outputs in `BloodCraftHub\bin\Release\net6.0\`:
   - `BloodCraftHub.dll` (and any other DLLs you ship)
   - `manifest.json` (auto-generated; pulled in from `obj\Release\net6.0\manifest.json` — copy it to the package root)
3. Copy `README.md` and `icon.png` (see below for the icon) into a temp folder alongside the DLL and manifest.
4. Zip the **contents** of that folder (Ctrl+A inside, right-click → compress) — confirm the zip's root is files, not a folder.
5. Run the zip through the manifest validator to be safe.
6. Sign in at https://thunderstore.io → V Rising community → Upload Package. Pick your team namespace, drop the zip, publish.

### Option B — `tcli` (Thunderstore CLI) — automates A end to end

`tcli` reads `thunderstore.toml` (already present in `BloodCraftHub/thunderstore.toml`) and handles zip assembly + upload.

```powershell
# Install once:
dotnet tool install --global tcli

# Build the .dll first
dotnet build BloodCraftHub\BloodCraftHub.csproj -c Release

# Stage the build output into the package directory.
# tcli expects: package/icon.png, package/README.md, package/<dll-and-assets>
# A small staging script can be added under tools/ later.

# Then from BloodCraftHub/ (where thunderstore.toml lives):
tcli build
tcli publish --token <your-service-account-token>
```

Service-account tokens are created at https://thunderstore.io/settings/teams/ once you have a team. Treat them like an API key — never commit, store in env (`TCLI_AUTH_TOKEN` is the convention).

### Option C — programmatic upload via REST API

The full Swagger spec is at https://thunderstore.io/api/docs/ (login required for the protected upload endpoints). Useful when wiring CI/CD; for our scope (a hobby mod), `tcli` is the right call.

## What we still need before first publish

- [ ] `icon.png` (256×256 PNG) — put it at `BloodCraftHub/icon.png` and also copy into the package zip. Neither upstream mod's icon is reusable as-is.
- [ ] `<PackageProjectUrl>` in `BloodCraftHub.csproj` — set to the GitHub repo URL once we publish one. Until then leave empty (the manifest emits `""`, which is valid).
- [ ] Decide on a Thunderstore team name. The current `thunderstore.toml` uses `kdpen` — claim it at https://thunderstore.io/settings/teams/ if it's not taken, otherwise pick another and update both `thunderstore.toml` and any docs that reference it.
- [ ] Pick a license (see `LICENSE.txt`) — required by some package guidelines and good practice.
- [ ] Manual sanity pass through the manifest validator after the first build.

## Version-bump checklist (every release)

1. Bump `<Version>` in `BloodCraftHub/BloodCraftHub.csproj`.
2. Bump `versionNumber` in `BloodCraftHub/thunderstore.toml` to match.
3. Update `CHANGELOG.md`.
4. Rebuild Release → re-validate manifest → upload.

Thunderstore does **not** allow re-uploading the same version number; each upload is immutable. If the upload fails for any reason, bump the patch and try again.

## Failure modes worth knowing

- **"Invalid name"** — non-allowed character in `name` (underscores OK, hyphens and dots not).
- **"Files not at root"** — fix the zipping step (see foot-gun above).
- **"Dependency not found"** — the team-package-version triple has to exactly match an existing Thunderstore package. Search the V Rising community page for the BepInExPack version you're pinning.
- **Icon rejected** — must be 256×256 *exactly*. Resize with any image tool; don't trust the OS preview to confirm.
- **Manifest JSON error** — usually a missing `website_url: ""`. The field is mandatory even when empty.

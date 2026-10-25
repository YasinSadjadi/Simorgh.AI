---
name: release-nuget
description: Cut a new Simorgh.AI release — verify the build and tests, bump the version, commit, tag, push, and confirm the packages landed on nuget.org. Use when the user asks to release a new version, publish to NuGet, tag a release, or bump the package version.
---

# Release Simorgh.AI to NuGet

Releases are tag-driven: pushing a tag `v*` to `origin` triggers the
"Build & Publish NuGet" workflow (`.github/workflows/dotnet.yml`), which builds,
packs, and publishes all three packages (`Simorgh.AI.Abstractions`,
`Simorgh.AI.Core`, `Simorgh.AI.Extensions`) to nuget.org via trusted publishing.
The publish job runs only for tag pushes; branch pushes only build.

## Steps

1. **Collect the state.**
   - Ask for (or infer from the request) the new version number — semantic
     version, e.g. `0.1.2`.
   - `git status --short` — review uncommitted changes. Split unrelated
     changes into their own commits; only release-ready code should go into the
     release.

2. **Verify before releasing.** From the repo root:
   ```bash
   dotnet build -v q
   dotnet test -v q
   ```
   Both must pass with zero errors. If tests fail, stop and report — do not tag.

3. **Bump the version** in `Directory.Build.props` (single source for all
   packages):
   ```xml
   <Version>0.1.2</Version>
   ```

4. **Commit and tag:**
   ```bash
   git add Directory.Build.props
   git commit -m "Release 0.1.2"
   git tag v0.1.2
   ```

5. **Push and trigger the release** (always confirm with the user before
   pushing — this publishes publicly):
   ```bash
   git push origin main v0.1.2
   ```

6. **Watch the workflow** (`gh` may not be authenticated; the public API works):
   ```bash
   curl -s "https://api.github.com/repos/YasinSadjadi/Simorgh.AI/actions/runs?per_page=3" \
     | grep -E '"name"|"status"|"conclusion"'
   ```
   Wait until the run for the tag shows `"status": "completed"`. If the
   conclusion is not `success`, read the run's logs and report the failure.

7. **Verify the packages landed on NuGet.** Indexing lags a few minutes after a
   successful publish; re-check with a short wait if needed:
   ```bash
   curl -s "https://api.nuget.org/v3-flatcontainer/simorgh.ai.core/index.json"
   ```
   Confirm the new version appears for all three package ids:
   `simorgh.ai.abstractions`, `simorgh.ai.core`, `simorgh.ai.extensions`.

## Notes

- Never re-tag or move an existing published tag — push a new patch version
  instead.
- The version in `Directory.Build.props` is the only place to bump; package ids
  come from project names.
- If the user only asks to commit (no release), stop after step 1–2 and their
  requested commits; tagging/pushing is only for releases.

---
name: fiftyroutine-release
description: >-
  Packages FiftyRoutine (Personal_Management Desktop) as a portable win-x64 zip,
  commits the release with code changes, and pushes to origin. Use when the user
  asks to 封装安装包, 打包发布, make a release zip, or run the FiftyRoutine
  release/commit/push flow.
---

# FiftyRoutine release pack

Portable「解压即用」zip（非 Program Files 安装器）。数据根在 exe 同级；框架依赖 .NET 8 Windows Desktop。

## Checklist

```
- [ ] 1. Version + publish + zip
- [ ] 2. Commit (code + zip only)
- [ ] 3. Push origin
```

## Step 1 — Publish and zip

Repo root = workspace that contains `Personal_Management/` and `FiftyRoutineRelease/`.

1. Pick next semver from existing `FiftyRoutineRelease/FiftyRoutine-v*-win-x64.zip` (bump patch unless user names a version). Example: `0.0.2` → `0.0.3`.
2. Publish to a **temp** folder under `FiftyRoutineRelease/` (never leave `_publish` in git):

```powershell
$root = "<repo-root>"
$ver = "<x.y.z>"
$pub = Join-Path $root "FiftyRoutineRelease\_publish"
$zipPath = Join-Path $root "FiftyRoutineRelease\FiftyRoutine-v$ver-win-x64.zip"
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
New-Item -ItemType Directory -Path $pub | Out-Null
dotnet publish "$root\Personal_Management\Desktop\Desktop.csproj" `
  -c Release -r win-x64 --self-contained false -o $pub --nologo
@('UserData','ProgramData') | ForEach-Object {
  $p = Join-Path $pub $_
  if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Push-Location $pub
tar -a -cf $zipPath *
Pop-Location
Remove-Item $pub -Recurse -Force
```

3. Confirm zip lists `PersonalManagement.exe` at **archive root** (with `Assets/…`), size ~6–7MB. No `UserData` / `ProgramData` / `nocodb-data`.

### Rules

- **Folder publish**, not single-file (`--self-contained false`).
- **Do not** MSI / Inno / Program Files installers unless user explicitly asks.
- `.gitignore` keeps only `FiftyRoutineRelease/**/*.zip`; do not commit `_publish`.

## Step 2 — Commit

Use `D:\tools\Git\bin\git.exe` when on this machine (wrapper/`--trailer` can break older git).

1. Stage: product code + docs touched by the release (`Personal_Management/Desktop/…`, `窗口信息.md`, iteration docs if updated) + **new** `FiftyRoutineRelease/FiftyRoutine-v*.zip`.
2. **Do not** stage: `nocodb-data/**`, local `UserData`/`ProgramData`, secrets, `_publish`.
3. Commit message style (English, why-focused), e.g.:

```
Add Gadgets mirror-text page and FiftyRoutine v0.0.3 win-x64 zip.
```

Follow the repo’s normal commit protocol (status/diff/log → stage → commit → status). Only commit when the user asked for commit (this skill implies they did).

## Step 3 — Push

```powershell
D:\tools\Git\bin\git.exe -C "<repo-root>" push -u origin HEAD
```

Remote is typically `https://github.com/OnionContainer/FiftyRoutine.git`. Report the zip path and push result.

## Do not

- Bump version without checking existing zips.
- Commit runtime DB / uploads under `nocodb-data/`.
- Self-contained or single-file publish unless requested.

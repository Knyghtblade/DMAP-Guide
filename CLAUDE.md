# Working on DMAP Guide

## Who you're working with
Paul is a TV producer, not a programmer. Explain things in plain words at the "double-click this"
level. No command lines, no developer jargon, no file paths in prose unless he has to go there.
Do the technical work; describe the result and what to do next. Anything written for colleagues
(READ ME.txt, messages in the app) is read by non-technical people too.

## Direction (decided 18 Oct 2026)
- The product is the **Windows desktop app shipped as one file in a shared OneDrive folder**:
  `DMAP Guide.exe` + a `guide` folder beside it. No server, no installer, no settings for colleagues.
- The phone app (`DmapAndroid`) and the server (`DmapApi`) are **paused**, kept in the repo (but
  `DmapAndroid` also builds the Mac app, see Status). The
  phone app still shares `DmapShared`, so keep it compiling and give it the same features
  (buttons, warnings) when a change is cheap, so the two stay identical.
- Build the shared folder with `Publish Shared App.cmd` (bumps the version, see below). Only the
  new exe and READ ME.txt are copied into OneDrive afterwards, never the `guide` folder.

## Where the shared guide lives on Paul's home PC
`C:\Users\PaulE\OneDrive - Hasbro Inc\DMAP Guide\Dungeon Masters Production Guide\`
(the shared folder is the outer `DMAP Guide`; the app and `guide` folder are inside the inner one).

## Status (as of 22 Oct 2026)
- Sharing works: the team is editing the shared guide and sees each other's changes (Paul, 22 Oct).
  Earlier failures were all colleagues running a copy of the exe outside a OneDrive-synced folder;
  the app now refuses to open in that case with a page saying what to do.
- 1.0.3 (shipped, Paul confirmed it works): every addable thing can be deleted. While editing, the
  page header shows "Delete episode/campaign/season"; people, characters, key moments and air
  dates have a ✕; portraits have "Remove picture"; cover art has "Remove".
- The phone-app half of 1.0.3 is unbuilt: the PC used on 22 Oct has no `maui-android` workload.
- 1.0.4 (27 Oct): the **web copy**, a read-only single HTML file of the whole guide that looks and
  moves like the app (cover doors, sparkles, page turns, candlelight; cast ↔ character links).
  Built by `DmapShared\Export\WebCopy.cs`. The app rewrites `DMAP Guide (web copy).html` beside
  the exe when someone who saved changes closes it (shared folder only); Settings also has
  "Save a web copy…". Only the app writes it. OneDrive/SharePoint download HTML rather than show
  it, so readers open the downloaded file; a true "click a link" site would need Hasbro IT.
- 1.0.7 (27 Oct): the app's Cast page shows "Runs" (campaign covers) and "Plays" (character
  portraits, for everyone incl. guest stars) as links; the Characters page links "Played by" to
  the Cast page. Also fixed a crash on every close with nothing to save (WPF `Close()` called
  inside `Closing`); Windows' Application event log showed it for every version before 1.0.7.
- 1.0.8 (27 Oct): **Mac version**, wanted by Paul and built by a colleague on their Mac. It is
  `DmapAndroid` (MAUI) built for Mac Catalyst (`-p:DmapBuildMacCatalyst=true`); it opens the guide
  folder picked in Settings (picking the shared folder opens its `guide`; empty folders are
  refused). Its version is read from `DmapGuide.csproj`. The MAUI screens were brought up to the
  Windows app: Cast page (Runs/Plays), Shoot Notes, guest stars from the Cast page, Played-by link,
  air-date Watch links. `Make Mac Build Kit.cmd` makes `publish\DMAP Guide - Mac build kit <ver>.zip`
  (instructions: `MAC BUILD - START HERE.txt`); rebuild and resend it after each Windows release.
  Never built on a real Mac yet: checked by building the same screens for Windows
  (`maui-windows` workload, scratch copy with Platforms/Windows added) and running them.
- 1.0.9 (28 Oct): the **Introduction - What is DMAP?** page (top of the menu, one editable text
  box kept in the show-wide record, its own edit lock), on Windows, Mac and in the web copy.
  It came from GitHub, where another PC had branched before 1.0.3 and also made its own Cast ↔
  Characters links; the merge kept this PC's links and took only the Introduction commit (ad876ed).

## What the shipped app checks and shows
- Published (single-file) build only: refuses to open unless a `guide` folder sits beside the exe
  **and** that folder is inside a folder OneDrive syncs on that PC (`AppSettings.NotSyncedProblem`).
  The dev build (`Run DMAP Guide.cmd`) still uses the folder in Settings.
- "⟳ Refresh" button / F5: saves, then picks up others' changes; status line says what happened.
- Version: `<Version>` in `DmapGuide\DmapGuide.csproj`; the publish script raises the last number.
  Shown in the title bar and Settings. The guide folder holds `app-version.txt`, the newest
  version that opened it; older apps show a red warning to close and reopen from the shared folder.

## Checking work
`dotnet test` (DmapTests) must pass; build `DmapGuide` and, when shared code changed, `DmapAndroid`.
To see the real shipped behaviour, run the published exe (the dev build skips the shared-folder checks).

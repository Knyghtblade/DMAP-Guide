# Working on DMAP Guide

## Who you're working with
Paul is a TV producer, not a programmer. Explain things in plain words at the "double-click this"
level. No command lines, no developer jargon, no file paths in prose unless he has to go there.
Do the technical work; describe the result and what to do next. Anything written for colleagues
(READ ME.txt, messages in the app) is read by non-technical people too.

## Direction (decided 18 Oct 2026)
- The product is the **Windows desktop app shipped as one file in a shared OneDrive folder**:
  `DMAP Guide.exe` + a `guide` folder beside it. No server, no installer, no settings for colleagues.
- The phone app (`DmapAndroid`) and the server (`DmapApi`) are **paused**, kept in the repo. The
  phone app still shares `DmapShared`, so keep it compiling and give it the same features
  (buttons, warnings) when a change is cheap, so the two stay identical.
- Build the shared folder with `Publish Shared App.cmd` (bumps the version, see below). Only the
  new exe and READ ME.txt are copied into OneDrive afterwards, never the `guide` folder.

## Where the shared guide lives on Paul's home PC
`C:\Users\PaulE\OneDrive - Hasbro Inc\DMAP Guide\Dungeon Masters Production Guide\`
(the shared folder is the outer `DMAP Guide`; the app and `guide` folder are inside the inner one).

## Open problem (as of 19 Oct 2026)
Colleagues who open the app can't see Paul's changes and he can't see theirs. Paul's side is
verified healthy (OneDrive synced, pinned, no conflicts). Every failure so far traced to the
colleague running a copy of the exe that isn't inside a OneDrive-synced folder: a download from
the OneDrive website, or the whole folder downloaded/unzipped. The app now refuses to open in
those cases with a page that says what to do. Next step: a fresh colleague follows READ ME.txt
with version 1.0.1 or later; if they get a refusal page, they are on a copy; if the app opens,
their Settings "Kept in" path must contain "OneDrive - Hasbro Inc" and end in `\guide`.

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

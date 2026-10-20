# DMAP Guide

The production guide to *Dungeon Masters*, made to look like a tome. It has seasons, campaigns, the cast, each campaign's characters, episodes with shoot notes, filming and air dates. Several people can edit it at once.

## Projects

| Folder | What it is |
|---|---|
| `DmapGuide` | The Windows desktop app (WPF). |
| `DmapAndroid` | The Android app (.NET MAUI). It's the same tome as the desktop, page for page. |
| `DmapShared` | Everything both apps share: the guide's pages, the view model behind every page, and the two places a guide can live (see below). Change behaviour here, so the apps stay identical. |
| `DmapApi` | The shared guide server (ASP.NET Core and SQLite). See [DmapApi/README.md](DmapApi/README.md). |
| `DmapTests` | Tests for the server, for syncing between two people, and for the shared view model. Run `dotnet test`. |

## Where a guide lives

- **A folder** (the desktop app). It holds one small file per page. The app opens the `guide` folder beside `DMAP Guide.exe` when there is one (that's how the shared OneDrive folder works), otherwise the folder chosen in Settings. The desktop never needs a server or an access key. A folder shared as view-only opens read-only, without the Edit button.
- **The shared server**, for the phone app (paused for now). It holds one record per page.

Both work the same way:
- Only pages that changed are saved.
- If someone else saved a page since you opened it, you're asked whose version to keep.
- Other people's changes appear while the guide is open.
- You're warned if someone else is editing the page you open.

Choose between them on the **Settings** page.

## Sharing with the team

Run `Publish Shared App.cmd`. It builds `publish\DMAP Guide\` with a single `DMAP Guide.exe` (nothing to install), a `guide` folder and a `READ ME.txt` for colleagues. Copy that folder into the work OneDrive and share it: "Can view" for readers, "Can edit" for the people who maintain the guide. Colleagues add a shortcut to the shared folder in OneDrive and double-click the exe. To ship an update, run the script again and replace the exe (only the exe, never the `guide` folder).

The shipped exe only opens the `guide` folder beside it, and only when that folder is inside one OneDrive syncs on that PC. Otherwise it shows a page explaining that it was opened from a download or an unsynced copy, and how to fix that. Each published build gets a new version number (the title bar and Settings show it); the guide folder remembers the newest version that opened it, and an older app warns its user to reopen from the shared folder. The Refresh button (F5) saves and picks up everyone else's changes on demand.

## Running it while developing

- **Desktop:** double-click `Run DMAP Guide.cmd`. It opens the folder in Settings (or `--data <folder>` for a different one).
- **Tests:** `dotnet test`.
- **Phone and server (paused):** the `DmapAndroid` and `DmapApi` projects are kept but not in use. The server runs with `dotnet run --project DmapApi -- --urls http://127.0.0.1:5080`; see `DmapAndroid\ANDROID PHONE TEST - START HERE.txt` for the phone.

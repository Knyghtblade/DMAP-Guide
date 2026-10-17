# DMAP Guide

A producer's guide to *Dungeon Masters*, made to look like a tome. It has seasons, campaigns, cast and characters, episodes, filming and air dates. Several people can edit it at once.

## Projects

| Folder | What it is |
|---|---|
| `DmapGuide` | The Windows desktop app (WPF). |
| `DmapAndroid` | The Android app (.NET MAUI). It's the same tome as the desktop, page for page. |
| `DmapShared` | Everything both apps share: the guide's pages, the view model behind every page, and the two places a guide can live (see below). Change behaviour here, so the apps stay identical. |
| `DmapApi` | The shared guide server (ASP.NET Core and SQLite). See [DmapApi/README.md](DmapApi/README.md). |
| `DmapTests` | Tests for the server, for syncing between two people, and for the shared view model. Run `dotnet test`. |

## Where a guide lives

- **A folder** (desktop only), for example a shared OneDrive folder. It holds one small file per page.
- **The shared server**, for the desktop and phone apps. It holds one record per page.

Both work the same way:
- Only pages that changed are saved.
- If someone else saved a page since you opened it, you're asked whose version to keep.
- Other people's changes appear while the guide is open.
- You're warned if someone else is editing the page you open.

Choose between them on the **Settings** page.

## Testing

- **This PC only:** run `Run Local Test Server.cmd`, then the desktop app (Settings → server `http://localhost:5080/`, key `local-dev-key`) or `Run Android Emulator Test.cmd`.
- **Another PC or a phone:** see `REMOTE TEST - START HERE.txt` and `DmapAndroid\ANDROID PHONE TEST - START HERE.txt`.

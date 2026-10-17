# DMAP API

The shared guide server. It keeps the guide one record per page (the show details, and each season, campaign and episode) in SQLite, along with the pictures and the "someone is editing this page" markers. The desktop and phone apps both use it through `DmapShared`.

## Where the data lives

`%LOCALAPPDATA%\DMAP Guide Server\` (`dmap.db` and `images\`), or the folder set in `Dmap:DataFolder`. The first time it starts, the server copies across a guide from the first server version (`bin\…\data\dmap.db`) if it finds one, leaving the old file untouched.

## Access keys

Every request needs an `X-Api-Key` header. Each key has its own role, `viewer` (read only), `editor` or `admin`. Nobody can choose their own role.

- **Local testing:** in Development, with no keys configured, `local-dev-key` (or `Dmap:ApiKey`) is accepted as an editor key. `Run Local Test Server.cmd` starts the server this way, listening only on this computer.
- **Everything else:** configure keys under `Dmap:ApiKeys` (each `Key` at least 16 characters, plus a `Role`). The server refuses to start outside Development without them. `RemoteTest\Run-Remote-API.cmd` uses a random key made by `Prepare Remote PC Test.cmd`.

```powershell
dotnet user-secrets set "Dmap:ApiKeys:0:Key" "a-long-random-key" --project .\DmapApi\DmapApi.csproj
dotnet user-secrets set "Dmap:ApiKeys:0:Role" "editor" --project .\DmapApi\DmapApi.csproj
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:Dmap__LoadUserSecrets = "true"
dotnet run --project .\DmapApi\DmapApi.csproj -- --urls http://127.0.0.1:5080
```

The apps send `X-User-Name` (URL-escaped) with each request. It's the name shown on pages ("Last changed by …"), not a login.

## Reaching it from other devices

Use Tailscale: run `tailscale serve --bg http://127.0.0.1:5080` and use the HTTPS tailnet address it prints. `Prepare Remote PC Test.cmd` does this for you. Don't forward the port on your router. The phone app only allows plain HTTP to the emulator's address for this PC (`10.0.2.2`).

## Endpoints

All of these are under `/api/dmap`:

| Method | Route | Purpose |
|---|---|---|
| GET | `health` | Server status, this key's role, guide version |
| GET | `guide` | Every page, plus the guide version |
| GET | `changes?since=N` | Pages changed or deleted after guide version N |
| PUT | `records/{kind}/{id}` | Save a page: `{ baseVersion, parentId, data }`. Use `baseVersion` 0 to create. Returns 409 with the current page if someone else saved it first, or 404 if it was deleted. |
| DELETE | `records/{kind}/{id}` | Delete a page and the pages inside it |
| GET/PUT/DELETE | `locks/{id}` | "Someone is editing this page" markers |
| POST | `images?ext=.jpg` | Add a picture (the request body is the file, 15 MB at most) |
| GET | `images/{name}` | Fetch a picture |

`kind` is `guide`, `season`, `campaign` or `episode`. See `DmapApi.http` for examples.

A company deployment should use the company's identity provider and managed hosting rather than shared keys.

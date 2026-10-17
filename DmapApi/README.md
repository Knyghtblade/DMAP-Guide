# DMAP API

The API uses SQLite for the structured guide and keeps the whole-guide JSON document synchronized for compatibility.

## Desktop test

Start the API from the repository root. Use a long random key in place of the example and keep it private.

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:Dmap__ApiKey = "replace-with-a-long-random-key"
dotnet run --project .\DmapApi\DmapApi.csproj -- --urls http://127.0.0.1:5080
```

Open the settings file with `notepad "$env:APPDATA\DMAP Guide\settings.json"`. Set these properties, preserving existing `DataFolder` and `UserName` values:

```json
{
	"ApiBaseUrl": "http://localhost:5080/",
	"ApiKey": "replace-with-the-same-random-key",
	"ApiRole": "editor"
}
```

Restart DMAP Guide. The API is tried first; if it has no guide yet, the app loads the local guide folder. The first desktop save uploads that guide. Open `http://localhost:5080/swagger` to check the API is running. Swagger requests do not automatically send `X-Api-Key`, so operations sent from Swagger will return `401` unless a client adds that header.

## Same Wi-Fi

Stop the API and restart it listening on the desktop's network interfaces:

```powershell
dotnet run --project .\DmapApi\DmapApi.csproj -- --urls http://0.0.0.0:5080
```

If Windows Firewall prompts, allow access only on Private networks. Otherwise add a Private-profile inbound rule for TCP port `5080`; do not disable the firewall. Run `ipconfig` and note the desktop's IPv4 address, for example `192.168.1.25`.

On the phone, open `http://192.168.1.25:5080/swagger` using that address. To call API endpoints, use a mobile REST client and set `X-Api-Key` to the same random key. Write requests also need `X-User-Role: editor` and `Content-Type: application/json`. A direct browser visit to `/api/dmap/health` returns `401` because it cannot add the required custom header.

The phone and desktop must be on the same non-guest Wi-Fi network, and client isolation must be disabled on the router.

## Outside Development

The API refuses to start until role-bound keys are configured. Each indexed entry under `Dmap:ApiKeys` has a `Key` secret value and a `Role` value. Production requests cannot choose their role through `X-User-Role`; the API key determines it. Store production keys in the deployment secret manager.

## Internet test

Install Tailscale on the desktop and phone, and sign in to the same private tailnet. Do not forward API ports on the router. Configure a role-bound key for a non-Development test:

```powershell
dotnet user-secrets set "Dmap:ApiKeys:0:Key" "replace-with-a-different-long-random-key" --project .\DmapApi\DmapApi.csproj
dotnet user-secrets set "Dmap:ApiKeys:0:Role" "editor" --project .\DmapApi\DmapApi.csproj
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:Dmap__LoadUserSecrets = "true"
dotnet run --project .\DmapApi\DmapApi.csproj -- --urls http://127.0.0.1:5080
```

In a second PowerShell terminal, run `tailscale serve --bg http://127.0.0.1:5080`. Tailscale prints an HTTPS tailnet URL. From the phone REST client, send `GET https://<tailnet-hostname>/api/dmap/health` with `X-Api-Key` set to the role-bound key. Use the HTTPS tailnet URL as the desktop `ApiBaseUrl` too if you want the desktop app to use the tunnel. Production ignores `X-User-Role`.

These API keys are for a private proof-of-concept, not company single sign-on. A company deployment should use its identity provider and managed hosting. This repository does not yet contain a phone app; the phone can check the API and send REST requests, but cannot use the DMAP guide UI yet.

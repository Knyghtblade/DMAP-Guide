using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace DmapShared.Api;

/// <summary>Talks to the DMAP server. Errors come back as <see cref="DmapApiException"/> with a message fit to show.</summary>
public sealed class DmapApiClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    private readonly HttpClient _http;

    public Uri BaseAddress { get; }

    /// <param name="handler">For tests; normally null.</param>
    public DmapApiClient(string baseUrl, string accessKey, string userName, HttpMessageHandler? handler = null)
    {
        if (!TryNormalizeAddress(baseUrl, out var address))
            throw new DmapApiException(null, "The server address isn't valid. It should look like https://name.tailnet.ts.net/");
        if (string.IsNullOrWhiteSpace(accessKey))
            throw new DmapApiException(null, "Enter the access key.");

        BaseAddress = address;
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = address;
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Add(ApiHeaders.ApiKey, accessKey.Trim());
        _http.DefaultRequestHeaders.Add(ApiHeaders.UserName, Uri.EscapeDataString(userName ?? ""));
    }

    /// <summary>Accepts "host", "host:port", "http(s)://host/"; always ends with a slash.</summary>
    public static bool TryNormalizeAddress(string? text, out Uri address)
    {
        address = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (!text.Contains("://")) text = "https://" + text;
        if (!text.EndsWith('/')) text += "/";
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return false;
        address = uri;
        return true;
    }

    public Task<ServerInfo> GetInfoAsync(CancellationToken ct = default) =>
        GetAsync<ServerInfo>("api/dmap/health", ct);

    public Task<GuideSnapshot> GetGuideAsync(CancellationToken ct = default) =>
        GetAsync<GuideSnapshot>("api/dmap/guide", ct);

    public Task<GuideChanges> GetChangesAsync(long since, CancellationToken ct = default) =>
        GetAsync<GuideChanges>($"api/dmap/changes?since={since}", ct);

    /// <summary>Save a page. Returns the saved page, or the server's current page if someone else saved it first.</summary>
    public async Task<SaveOutcome> PutRecordAsync(string kind, string id, string? parentId, long baseVersion, string json, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(json);
        var body = new RecordWrite { BaseVersion = baseVersion, ParentId = parentId, Data = doc.RootElement.Clone() };
        using var response = await Send(() => _http.PutAsJsonAsync(RecordUrl(kind, id), body, Json, ct)).ConfigureAwait(false);

        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
            case HttpStatusCode.Created:
                return SaveOutcome.Saved((await Read<RecordEnvelope>(response, ct).ConfigureAwait(false))!);
            case HttpStatusCode.Conflict:
                var conflict = await Read<ConflictResponse>(response, ct).ConfigureAwait(false);
                if (conflict?.Current != null) return SaveOutcome.Conflict(conflict.Current);
                break;
            case HttpStatusCode.NotFound:
                return SaveOutcome.Gone();
        }
        throw await Failure(response, ct).ConfigureAwait(false);
    }

    /// <summary>Delete a page and the pages inside it. Already gone counts as done.</summary>
    public async Task DeleteRecordAsync(string kind, string id, CancellationToken ct = default)
    {
        using var response = await Send(() => _http.DeleteAsync(RecordUrl(kind, id), ct)).ConfigureAwait(false);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound) return;
        throw await Failure(response, ct).ConfigureAwait(false);
    }

    public async Task<EditLockMessage?> GetLockAsync(string id, CancellationToken ct = default)
    {
        using var response = await Send(() => _http.GetAsync($"api/dmap/locks/{Uri.EscapeDataString(id)}", ct)).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        if (!response.IsSuccessStatusCode) throw await Failure(response, ct).ConfigureAwait(false);
        return await Read<EditLockMessage>(response, ct).ConfigureAwait(false);
    }

    public async Task PutLockAsync(string id, EditLockMessage message, CancellationToken ct = default)
    {
        using var response = await Send(() => _http.PutAsJsonAsync($"api/dmap/locks/{Uri.EscapeDataString(id)}", message, Json, ct)).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw await Failure(response, ct).ConfigureAwait(false);
    }

    public async Task DeleteLockAsync(string id, Guid session, CancellationToken ct = default)
    {
        using var response = await Send(() => _http.DeleteAsync($"api/dmap/locks/{Uri.EscapeDataString(id)}?session={session}", ct)).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw await Failure(response, ct).ConfigureAwait(false);
    }

    /// <summary>Upload a picture; returns the path to store on the page (e.g. "images/1a2b….jpg").</summary>
    public async Task<string> UploadImageAsync(Stream content, string extension, CancellationToken ct = default)
    {
        using var body = new StreamContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await Send(() => _http.PostAsync($"api/dmap/images?ext={Uri.EscapeDataString(extension)}", body, ct)).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw await Failure(response, ct).ConfigureAwait(false);
        return (await Read<ImageUploadResult>(response, ct).ConfigureAwait(false))!.Path;
    }

    /// <summary>Download a picture to <paramref name="targetFile"/>. Returns false if the server doesn't have it.</summary>
    public async Task<bool> DownloadImageAsync(string name, string targetFile, CancellationToken ct = default)
    {
        using var response = await Send(() => _http.GetAsync($"api/dmap/images/{Uri.EscapeDataString(name)}", HttpCompletionOption.ResponseHeadersRead, ct)).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        if (!response.IsSuccessStatusCode) throw await Failure(response, ct).ConfigureAwait(false);

        string tmp = targetFile + ".download";
        await using (var file = File.Create(tmp))
            await response.Content.CopyToAsync(file, ct).ConfigureAwait(false);
        File.Move(tmp, targetFile, overwrite: true);
        return true;
    }

    private static string RecordUrl(string kind, string id) =>
        $"api/dmap/records/{Uri.EscapeDataString(kind)}/{Uri.EscapeDataString(id)}";

    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        using var response = await Send(() => _http.GetAsync(url, ct)).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw await Failure(response, ct).ConfigureAwait(false);
        return (await Read<T>(response, ct).ConfigureAwait(false))!;
    }

    private async Task<HttpResponseMessage> Send(Func<Task<HttpResponseMessage>> send)
    {
        try
        {
            return await send().ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new DmapApiException(null,
                $"Couldn't reach the shared guide at {BaseAddress}. Check that this device is online (and connected to Tailscale), and that the guide server is running.", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new DmapApiException(null,
                $"The shared guide at {BaseAddress} didn't answer in time. Check the connection and try again.", ex);
        }
    }

    private static async Task<T?> Read<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new DmapApiException(response.StatusCode,
                "The server's reply wasn't understood. Check the server address points at the DMAP guide server.", ex);
        }
    }

    private static async Task<DmapApiException> Failure(HttpResponseMessage response, CancellationToken ct)
    {
        string? detail = null;
        try
        {
            detail = (await response.Content.ReadFromJsonAsync<ErrorResponse>(Json, ct).ConfigureAwait(false))?.Error;
        }
        catch (Exception) { }

        string message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "The access key wasn't accepted. Check it with the person who runs the shared guide.",
            HttpStatusCode.Forbidden => "This access key can only read the guide, not change it.",
            HttpStatusCode.NotFound => "The server didn't recognise that request. Check the server address points at the DMAP guide server.",
            _ when !string.IsNullOrWhiteSpace(detail) => detail!,
            _ => $"The server reported a problem ({(int)response.StatusCode} {response.ReasonPhrase})."
        };
        return new DmapApiException(response.StatusCode, message);
    }

    public void Dispose() => _http.Dispose();
}

public sealed class DmapApiException : Exception
{
    /// <summary>Null when the server couldn't be reached at all.</summary>
    public HttpStatusCode? StatusCode { get; }

    public DmapApiException(HttpStatusCode? statusCode, string message, Exception? inner = null) : base(message, inner) =>
        StatusCode = statusCode;
}

public sealed class SaveOutcome
{
    public RecordEnvelope? Record { get; private init; }
    public bool IsConflict { get; private init; }

    /// <summary>The page no longer exists on the server (someone deleted it).</summary>
    public bool IsGone { get; private init; }

    public static SaveOutcome Saved(RecordEnvelope record) => new() { Record = record };
    public static SaveOutcome Conflict(RecordEnvelope current) => new() { Record = current, IsConflict = true };
    public static SaveOutcome Gone() => new() { IsGone = true };
}

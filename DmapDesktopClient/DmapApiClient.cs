using System.Net.Http.Json;
using System.Text;

namespace DmapDesktopClient;

public sealed class DmapApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _apiRole;

    public int? GuideVersion { get; private set; }

    public DmapApiClient(string baseUrl, string apiKey, string apiRole = "editor")
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("Base URL is required.", nameof(baseUrl));
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("API key is required.", nameof(apiKey));

        _apiKey = apiKey;
        _apiRole = string.IsNullOrWhiteSpace(apiRole) ? "editor" : apiRole;
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        _httpClient.DefaultRequestHeaders.Add("X-Api-Key", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("X-User-Role", _apiRole);
    }

    public DmapApiClient(HttpClient httpClient, string apiKey, string apiRole = "editor")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _apiRole = string.IsNullOrWhiteSpace(apiRole) ? "editor" : apiRole;
        _httpClient.DefaultRequestHeaders.Add("X-Api-Key", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("X-User-Role", _apiRole);
    }

    public async Task<string> GetGuideJsonAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("api/dmap/guide", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        UpdateGuideVersion(response);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveGuideJsonAsync(string json, int? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "api/dmap/guide")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var version = expectedVersion ?? GuideVersion;
        if (version.HasValue)
            request.Headers.TryAddWithoutValidation("X-Guide-Version", version.Value.ToString());
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        UpdateGuideVersion(response);
    }

    public async Task<IReadOnlyList<ShowDto>> GetShowsAsync(CancellationToken cancellationToken = default)
        => await GetAsync<IReadOnlyList<ShowDto>>("api/Dmap/shows", cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<SeasonDto>> GetSeasonsAsync(CancellationToken cancellationToken = default)
        => await GetAsync<IReadOnlyList<SeasonDto>>("api/Dmap/seasons", cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<CampaignDto>> GetCampaignsAsync(CancellationToken cancellationToken = default)
        => await GetAsync<IReadOnlyList<CampaignDto>>("api/Dmap/campaigns", cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<EpisodeDto>> GetEpisodesAsync(CancellationToken cancellationToken = default)
        => await GetAsync<IReadOnlyList<EpisodeDto>>("api/Dmap/episodes", cancellationToken).ConfigureAwait(false);

    public async Task<EpisodeDto> GetEpisodeAsync(string id, CancellationToken cancellationToken = default)
        => await GetAsync<EpisodeDto>($"api/dmap/episodes/{Uri.EscapeDataString(id)}", cancellationToken).ConfigureAwait(false);

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync("api/Dmap/seed", null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<EpisodeDto> CreateEpisodeAsync(EpisodeDto episode, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/Dmap/episodes", episode, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        UpdateGuideVersion(response);
        return (await response.Content.ReadFromJsonAsync<EpisodeDto>(cancellationToken: cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<EpisodeDto> UpdateEpisodeAsync(EpisodeDto episode, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/Dmap/episodes/{episode.Id}", episode, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        UpdateGuideVersion(response);
        return (await response.Content.ReadFromJsonAsync<EpisodeDto>(cancellationToken: cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<SeasonDto> CreateSeasonAsync(SeasonDto season, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/dmap/seasons", season, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        UpdateGuideVersion(response);
        return (await response.Content.ReadFromJsonAsync<SeasonDto>(cancellationToken: cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<SeasonDto> UpdateSeasonAsync(SeasonDto season, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/dmap/seasons/{season.Id}", season, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        UpdateGuideVersion(response);
        return (await response.Content.ReadFromJsonAsync<SeasonDto>(cancellationToken: cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<CampaignDto> UpdateCampaignAsync(CampaignDto campaign, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/dmap/campaigns/{campaign.Id}", campaign, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        UpdateGuideVersion(response);
        return (await response.Content.ReadFromJsonAsync<CampaignDto>(cancellationToken: cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<CampaignDto> CreateCampaignAsync(CampaignDto campaign, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/dmap/campaigns", campaign, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        UpdateGuideVersion(response);
        return (await response.Content.ReadFromJsonAsync<CampaignDto>(cancellationToken: cancellationToken).ConfigureAwait(false))!;
    }

    public async Task DeleteSeasonAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"api/dmap/seasons/{Uri.EscapeDataString(id)}", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        UpdateGuideVersion(response);
    }

    public async Task DeleteCampaignAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"api/dmap/campaigns/{Uri.EscapeDataString(id)}", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        UpdateGuideVersion(response);
    }

    public async Task DeleteEpisodeAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"api/dmap/episodes/{Uri.EscapeDataString(id)}", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        UpdateGuideVersion(response);
    }

    private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(relativeUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        UpdateGuideVersion(response);
        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken).ConfigureAwait(false))!;
    }

    private void UpdateGuideVersion(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-Guide-Version", out var values) &&
            int.TryParse(values.FirstOrDefault(), out var version))
            GuideVersion = version;
    }

    public void Dispose() => _httpClient.Dispose();
}

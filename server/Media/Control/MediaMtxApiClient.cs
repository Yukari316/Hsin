using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Hsin.Media.Control;

public sealed class MediaMtxApiClient : IDisposable
{
    private readonly HttpClient http;

    public MediaMtxApiClient(ResolvedConfiguration configuration)
    {
        http = new HttpClient
        {
            BaseAddress = configuration.ControlApiUri,
            Timeout     = TimeSpan.FromSeconds(3)
        };

        ControlApiOptions control = configuration.Options.MediaMtx.ControlApi;
        if (!string.IsNullOrEmpty(control.Password))
        {
            string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(control.User + ":" + control.Password));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    public async Task<bool> HasLivePathConfigurationAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync("/v3/config/paths/get/live", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;

        response.EnsureSuccessStatusCode();
        using JsonDocument path = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        return path.RootElement.GetProperty("source").GetString() == "publisher";
    }

    public async Task<MediaPathSnapshot?> GetLivePathAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync("/v3/paths/get/live", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;

        response.EnsureSuccessStatusCode();
        using JsonDocument path = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        JsonElement root     = path.RootElement;
        bool        online   = root.GetProperty("online").GetBoolean();
        string?     sourceId = null;
        if (online && root.TryGetProperty("source", out JsonElement source) && source.ValueKind == JsonValueKind.Object)
            sourceId = source.TryGetProperty("id", out JsonElement id) ? id.GetString() : null;

        return new MediaPathSnapshot
        {
            Online   = online,
            SourceId = sourceId
        };
    }

    public void Dispose()
    {
        http.Dispose();
    }
}

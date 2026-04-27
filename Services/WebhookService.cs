using System.Text;
using System.Text.Json;

namespace Beauty.Api.Services;

public class WebhookService : IWebhookService
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger<WebhookService> _log;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WebhookService(IHttpClientFactory http, ILogger<WebhookService> log)
    {
        _http = http;
        _log  = log;
    }

    public async Task FireAsync(string url, object payload)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            var client = _http.CreateClient();
            var body   = JsonSerializer.Serialize(payload, _json);
            var resp   = await client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));

            if (!resp.IsSuccessStatusCode)
                _log.LogWarning("[Webhook] POST {Url} returned {Status}", url, (int)resp.StatusCode);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[Webhook] Failed to POST {Url}", url);
        }
    }
}

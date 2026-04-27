namespace Beauty.Api.Services;

public interface IWebhookService
{
    Task FireAsync(string url, object payload);
}

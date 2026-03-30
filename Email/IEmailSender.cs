namespace Beauty.Api.Email;

public interface IEmailSender
{
    Task SendHtmlAsync(
        string to,
        string subject,
        string html,
        string? fromOverride = null);

    Task SendHtmlWithAttachmentAsync(
        string to,
        string subject,
        string html,
        string fileName,
        byte[] content,
        string contentType,
        string? fromOverride = null);
}

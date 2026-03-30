using Azure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Graph;         // GraphServiceClient
using Microsoft.Graph.Models;  // Message, ItemBody, BodyType, Recipient, EmailAddress, FileAttachment

namespace Beauty.Api.Email;

public class GraphEmailSender : IEmailSender
{
    private readonly EmailOptions _opts;
    private readonly GraphServiceClient _graph;

    public GraphEmailSender(IOptions<EmailOptions> options)
    {
        _opts = options.Value;

        var credential = new ClientSecretCredential(
            _opts.TenantId,
            _opts.ClientId,
            _opts.ClientSecret);

        _graph = new GraphServiceClient(
            credential,
            new[] { "https://graph.microsoft.com/.default" });
    }

    public async Task SendHtmlAsync(
        string to, string subject, string html, string? fromOverride = null)
    {
        var message = BuildMessage(to, subject, html);
        await SendAsync(message, fromOverride);
    }

    public async Task SendHtmlWithAttachmentAsync(
        string to, string subject, string html,
        string fileName, byte[] content, string contentType, string? fromOverride = null)
    {
        var message = BuildMessage(to, subject, html);
        message.Attachments =
        [
            new FileAttachment
            {
                Name = fileName,
                ContentType = contentType,
                ContentBytes = content
            }
        ];
        await SendAsync(message, fromOverride);
    }

    private Message BuildMessage(string to, string subject, string html) =>
        new()
        {
            Subject = subject,
            Body = new ItemBody
            {
                ContentType = BodyType.Html,
                Content = html
            },
            ToRecipients =
            [
                new Recipient
                {
                    EmailAddress = new EmailAddress { Address = to }
                }
            ]
        };

    private async Task SendAsync(Message message, string? fromOverride)
    {
        var from = fromOverride ?? _opts.From;

        // v5 style: .Users[from].SendMail.PostAsync(new SendMailPostRequestBody { ... })
        await _graph.Users[from]
            .SendMail
            .PostAsync(new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
            {
                Message = message,
                SaveToSentItems = true
            });
    }
}
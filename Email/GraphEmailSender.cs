using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Graph;         // GraphServiceClient
using Microsoft.Graph.Models;  // Message, ItemBody, BodyType, Recipient, EmailAddress, FileAttachment

namespace Beauty.Api.Email;


public class GraphEmailSender : IEmailSender
{

    private static readonly string[] GraphScopes =
        {
        "https://graph.microsoft.com/.default"
    };

    private readonly EmailOptions _opts;
    private readonly ClientSecretCredential _credential;
    private readonly GraphServiceClient _graph;

    public GraphEmailSender(IOptions<EmailOptions> options)
    {
        _opts = options.Value;

        if (string.IsNullOrWhiteSpace(_opts.ClientId))
            throw new InvalidOperationException(
                "Email:ClientId is missing from configuration");

        Console.WriteLine("EMAIL AUTH CONFIG:");
        Console.WriteLine($"TenantId   = {_opts.TenantId}");
        Console.WriteLine($"ClientId   = {_opts.ClientId}");
        Console.WriteLine($"Secret set = {!string.IsNullOrWhiteSpace(_opts.ClientSecret)}");
        Console.WriteLine($"TenantId read from config: {_opts.TenantId}");


        _credential = new ClientSecretCredential(
            _opts.TenantId,
            _opts.ClientId,
            _opts.ClientSecret
        );

        _graph = new GraphServiceClient(
            _credential,
            GraphScopes
        );


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

    private static Message BuildMessage(string to, string subject, string html) =>
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

        await _graph.Users[from]
            .SendMail
            .PostAsync(new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
            {
                Message = message,
                SaveToSentItems = true
            });
    }
}
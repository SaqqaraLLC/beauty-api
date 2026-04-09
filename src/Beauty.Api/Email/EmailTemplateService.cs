using System.Collections.Generic;
using System.Threading.Tasks;

namespace Beauty.Api.Email
{
    public class EmailTemplateService
    {
        private readonly IEmailSender _sender;
        private readonly ITemplateRenderer _renderer;

        public EmailTemplateService(IEmailSender sender, ITemplateRenderer renderer)
        {
            _sender = sender;
            _renderer = renderer;
        }

        public Task SendWelcomeAsync(string to, string fullName, string loginUrl)
        {
            // ✅ Instantiate a concrete Dictionary (never "new IDictionary<,>")
            var data = new Dictionary<string, string>
            {
                ["FullName"] = fullName,
                ["LoginUrl"] = loginUrl
            };
            return Send("welcome", to, "Welcome to Saqqara LLC", data);
        }

        public Task SendResetAsync(string to, string userName, string resetUrl)
        {
            var data = new Dictionary<string, string>
            {
                ["UserName"] = userName,
                ["ResetUrl"] = resetUrl,
                ["Year"] = System.DateTime.UtcNow.Year.ToString()
            };
            return Send("password_reset", to, "Reset your password", data);
        }

        public Task SendAdminAsync(string to, string subject, string messageBody)
        {
            var data = new Dictionary<string, string>
            {
                ["MessageBody"] = messageBody,
                ["Timestamp"] = System.DateTime.UtcNow.ToString("u")
            };
            return Send("admin_alert", to, subject, data);
        }

        // Accept the interface so callers can pass any dictionary,
        // but ensure call sites construct "new Dictionary<string,string>"
        private async Task Send(
            string template,
            string to,
            string subject,
            IDictionary<string, string> data)
        {
            // Render is synchronous in your implementation
            var html = _renderer.Render(template, data);
            await _sender.SendHtmlAsync(to, subject, html);
        }
    }
}
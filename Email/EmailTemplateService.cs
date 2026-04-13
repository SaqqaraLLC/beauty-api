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
            var data = new Dictionary<string, string>
            {
                ["FullName"] = fullName,
                ["LoginUrl"] = loginUrl,
                ["Year"] = System.DateTime.UtcNow.Year.ToString()
            };
            return Send("welcome", to, "Welcome to Saqqara LLC", data);
        }

        public Task SendApplicationReceivedAsync(string to, string role)
        {
            var data = new Dictionary<string, string>
            {
                ["Email"] = to,
                ["Role"] = role,
                ["Year"] = System.DateTime.UtcNow.Year.ToString()
            };
            return Send("application_received", to, "Your Saqqara application has been received", data);
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

        public Task SendApprovedAsync(string to, string role)
        {
            var data = new Dictionary<string, string>
            {
                ["Email"] = to,
                ["Role"] = role,
                ["LoginUrl"] = "https://saqqarallc.com/login",
                ["Year"] = System.DateTime.UtcNow.Year.ToString()
            };
            return Send("application_approved", to, "Your Saqqara application has been approved", data);
        }

        public Task SendRejectedAsync(string to, string reason)
        {
            var data = new Dictionary<string, string>
            {
                ["Email"] = to,
                ["Reason"] = string.IsNullOrWhiteSpace(reason) ? "Your application did not meet our current requirements." : reason,
                ["Year"] = System.DateTime.UtcNow.Year.ToString()
            };
            return Send("application_rejected", to, "Update on your Saqqara application", data);
        }

        public Task SendAdminAsync(string to, string subject, string messageBody)
        {
            var data = new Dictionary<string, string>
            {
                ["MessageBody"] = messageBody,
                ["Timestamp"] = System.DateTime.UtcNow.ToString("u"),
                ["Year"] = System.DateTime.UtcNow.Year.ToString()
            };
            return Send("admin_alert", to, subject, data);
        }

        private async Task Send(
            string template,
            string to,
            string subject,
            IDictionary<string, string> data)
        {
            var html = _renderer.Render(template, data);
            await _sender.SendHtmlAsync(to, subject, html);
        }
    }
}
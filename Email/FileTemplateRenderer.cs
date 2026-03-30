using System.Text.Json;

namespace Beauty.Api.Email;

public sealed class FileTemplateRenderer : ITemplateRenderer
{
    private readonly IWebHostEnvironment _env;

    public FileTemplateRenderer(IWebHostEnvironment env)
    {
        _env = env;
    }

    public string Render(string templateName, object model)
    {
        var templatePath = Path.Combine(
            _env.ContentRootPath,
            "Email",
            "Templates",
            templateName);

        if (!File.Exists(templatePath))
            throw new FileNotFoundException($"Email template not found: {templatePath}");

        var html = File.ReadAllText(templatePath);

        // Very simple token replacement {{PropertyName}}
        foreach (var prop in model.GetType().GetProperties())
        {
            var token = $"{{{{{prop.Name}}}}}";
            var value = prop.GetValue(model)?.ToString() ?? string.Empty;
            html = html.Replace(token, value);
        }

        return html;
    }
}
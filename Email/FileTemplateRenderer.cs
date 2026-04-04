using System.Text.Json;

namespace Beauty.Api.Email;

public sealed class FileTemplateRenderer : ITemplateRenderer
{
    private readonly IWebHostEnvironment _env;

    public FileTemplateRenderer(IWebHostEnvironment env)
    {
        _env = env;
    }

    public string Render(string templateName, IDictionary<string, string> model)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "EmailTemplates",
            $"{templateName}.html"
        );

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Email template not found: {path}"
            );
        }

        var html = File.ReadAllText(path);

        foreach (var kv in model)
        {
            html = html.Replace(
                $"{{{{{kv.Key}}}}}",
                kv.Value ?? string.Empty,
                StringComparison.OrdinalIgnoreCase
            );
        }

        return html;
    }

}
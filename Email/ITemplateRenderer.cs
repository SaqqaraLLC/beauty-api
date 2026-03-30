namespace Beauty.Api.Email;

public interface ITemplateRenderer
{
    string Render(string templateName, object model);
}

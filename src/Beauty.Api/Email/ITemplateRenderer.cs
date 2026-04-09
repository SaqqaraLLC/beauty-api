using System.Collections.Generic;

namespace Beauty.Api.Email;

public interface ITemplateRenderer
{
    string Render(string templateName, IDictionary<string, string> model);
}

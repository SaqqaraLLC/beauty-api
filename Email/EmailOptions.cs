using System.Collections.Generic;
using System.Threading.Tasks;


namespace Beauty.Api.Email;

public class EmailOptions
{
    public string Provider { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    public string From { get; set; } = string.Empty;
    public string Support { get; set; } = string.Empty;

    public string AdminAlertsTo { get; set; } = string.Empty;
}

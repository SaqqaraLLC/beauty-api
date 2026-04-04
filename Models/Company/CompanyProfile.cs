namespace Beauty.Api.Models;

public class CompanyProfile
{
    public long Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string WebsiteUrl { get; set; } = "";
    public string LogoUrl { get; set; } = "";
}

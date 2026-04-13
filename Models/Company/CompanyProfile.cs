namespace Beauty.Api.Models;

public class CompanyProfile
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";   // FK → ApplicationUser
    public string CompanyName { get; set; } = "";
    public string Industry { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string WebsiteUrl { get; set; } = "";
    public string LogoUrl { get; set; } = "";
    public bool IsApproved { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

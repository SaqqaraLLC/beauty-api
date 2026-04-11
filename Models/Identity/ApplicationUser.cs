
using Beauty.Api.Models.Locations;
using Microsoft.AspNetCore.Identity;

namespace Beauty.Api.Models;

public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public long? ArtistId { get; set; }
    public long? LocationId { get; set; }
    public Artist? Artist { get; set; }
    public Location? Location { get; set; }
    public string Status { get; set; } = "Pending";
}




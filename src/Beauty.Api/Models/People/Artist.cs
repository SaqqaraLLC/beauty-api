using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models;

public class Artist
{
    public long ArtistId { get; set; }
    public string FullName { get; set; } = "";
    public string Specialty { get; set; } = "";
    
    [StringLength(1000)]
    public string? Bio { get; set; }
    
    [StringLength(500)]
    public string? ProfileImageUrl { get; set; }
    
    public bool Active { get; set; } = true;

    // Travel preferences — null means not set
    public bool? TravelNationwide { get; set; }      // true = will travel anywhere in the US
    public int?  TravelMaxMiles   { get; set; }      // max miles willing to travel (ignored when TravelNationwide = true)
}
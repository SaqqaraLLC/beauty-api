namespace Beauty.Api.Models;

public class Artist
{
    public long Id { get; set; }
    public string FullName { get; set; } = "";
    public string Specialty { get; set; } = "";
    public bool Active { get; set; } = true;
}
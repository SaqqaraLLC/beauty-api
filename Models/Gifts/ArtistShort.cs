namespace Beauty.Api.Models.Gifts;

public class ArtistShort
{
    public long   Id            { get; set; }
    public string ArtistUserId  { get; set; } = "";
    public string ArtistName    { get; set; } = "";
    public string Title         { get; set; } = "";
    public string VideoUrl      { get; set; } = "";
    public string? ThumbnailUrl { get; set; }
    public int    DurationSeconds { get; set; }
    public long   Views         { get; set; }
    public long   Likes         { get; set; }
    public bool   IsActive      { get; set; } = true;
    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
}

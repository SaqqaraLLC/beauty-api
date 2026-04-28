using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Moderation;

public class UserBlock
{
    public long Id { get; set; }

    [Required, MaxLength(450)]
    public string BlockerUserId { get; set; } = "";

    [Required, MaxLength(450)]
    public string BlockedUserId { get; set; } = "";

    [MaxLength(200)]
    public string? BlockedDisplayName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

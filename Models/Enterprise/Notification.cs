using System.ComponentModel.DataAnnotations;

namespace Beauty.Api.Models.Enterprise;

public class Notification
{
    [Key]
    public int NotificationId { get; set; }

    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = "";

    [Required]
    [MaxLength(100)]
    public string EventType { get; set; } = "";

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = "";

    [Required]
    [MaxLength(1000)]
    public string Body { get; set; } = "";

    [MaxLength(100)]
    public string? EntityType { get; set; }

    public int? EntityId { get; set; }

    [MaxLength(500)]
    public string? ActionUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsRead { get; set; } = false;
}
